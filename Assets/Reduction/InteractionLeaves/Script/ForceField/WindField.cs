using System;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;


namespace UnityEngine.PBD
{
    
    public enum ConvolutionMethod
    {
        LineSequence,
        LineSlidingWindow,
    }
    
    public unsafe class WindField : MonoBehaviour
    {
        
        // 边缘留一格
        // xzy
        private DoubleBuffer DensityField;

        private DoubleBuffer VelocityX;
        private DoubleBuffer VelocityY;
        private DoubleBuffer VelocityZ;

        //外部使用 back read， forn write
        public WindFiledExchangeBuffer exchange_Density;
        public WindFiledExchangeBuffer exchange_VelocityX;
        public WindFiledExchangeBuffer exchange_VelocityY;
        public WindFiledExchangeBuffer exchange_VelocityZ;
        
        private int3 dimensions;
        private int3 N;
        
        public int Length => dimensions.x * dimensions.y * dimensions.z;

        public int NSize => N.x * N.y * N.z;
        
        public int IteratorX => N.x;
        public int IteratorZ => N.z;
        public int IteratorY => N.y;

        public int3 Dim     => dimensions;
        public int3 PublicN => N;
        
        public bool IsCreated
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => exchange_Density.IsCreated   &&
                   exchange_VelocityX.IsCreated &&
                   exchange_VelocityY.IsCreated &&
                   exchange_VelocityZ.IsCreated;
        }

        private Transform m_windFieldCenter;
        
        private float3 m_lasFramePosition;
        private float3 m_selfMoveDelta;    //每一帧的位移 新
        private float3 m_selfMoveDeltaOld; //每一帧的位移 旧

        private PBDBounds m_windFieldBounds;
        private float3    m_windFieldOri;
        private float3    m_windScale;
        public  PBDBounds WindFieldBounds => m_windFieldBounds;// 外部用 N 过滤
        public  float3    WindFieldOri    => m_windFieldOri;//外部使用min 转换

        // public float3 windFieldSelfSpeed => m_selfSpeed;

        public float3 windFieldMoveDelta => m_selfMoveDeltaOld;

        private JobHandle _simlateJobHandle = default;

        public void Initialize(int3      dims,      float        diffusion, float viscosity,
                               Allocator allocator, in Transform center = null)
        {
            bool3 noPower2 = !math.ispow2(dims);
            if (math.any(noPower2))
            {
                if (noPower2.x)
                    dims.x = MathematicsUtil.NextPowerOfTwo(dims.x);
                if (noPower2.y)
                    dims.y = MathematicsUtil.NextPowerOfTwo(dims.y);
                if (noPower2.z)
                    dims.z = MathematicsUtil.NextPowerOfTwo(dims.z);
            }

            dimensions    = dims;
            N             = dims - 2;
            
            DensityField = new DoubleBuffer(dims, allocator, BoundaryType.Density);
            VelocityX    = new DoubleBuffer(dims, allocator,BoundaryType.VelocityX);
            VelocityY    = new DoubleBuffer(dims, allocator,BoundaryType.VelocityY);
            VelocityZ    = new DoubleBuffer(dims, allocator,BoundaryType.VelocityZ);

            int length = dimensions.x * dimensions.y * dimensions.z;
            
            exchange_Density   = new WindFiledExchangeBuffer(length, allocator);
            exchange_VelocityX = new WindFiledExchangeBuffer(length, allocator);
            exchange_VelocityY = new WindFiledExchangeBuffer(length, allocator);
            exchange_VelocityZ = new WindFiledExchangeBuffer(length, allocator);

            m_windFieldCenter = center ? center : transform;

            m_lasFramePosition = m_windFieldCenter.position;
        }

        public void Dispose()
        {
            DensityField.Dispose();
            VelocityX.Dispose();
            VelocityY.Dispose();
            VelocityZ.Dispose();
            if (IsCreated)
            {
                exchange_Density.Dispose();
                exchange_VelocityX.Dispose();
                exchange_VelocityY.Dispose();
                exchange_VelocityZ.Dispose();
            }
        }

        public void UpdateFieldParams(float deltaTime)
        {
            float3 currentPos = m_windFieldCenter.position + centerOffset;

            m_selfMoveDeltaOld = m_selfMoveDelta;

            m_selfMoveDelta = (currentPos - m_lasFramePosition);

            m_lasFramePosition = currentPos;

            float3 extends = 0.5f * (float3)N;

            //
            m_windFieldOri = currentPos - (extends) - 0.5f;//(0,0)

            m_windFieldBounds = new PBDBounds()
            {
                Min = -extends + currentPos,
                Max = extends + currentPos,
            };
        }

        //外部加力
        public JobHandle WriteToFrontBuffer(JobHandle dep, 
                                            ref NativeList<PBDForceField> forceFields,
                                            ref NativeList<PBDCustomColliderInfo> colliders)
        {
            if (IsCreated)
            {
                var writeDentsityJob = new WriteDensityFieldJob() { dim = dimensions, N = N };
                writeDentsityJob.UpdateParams(ref exchange_Density.front, in m_windFieldOri, ref colliders);
                
                var wirteVekocityJob = new WriteVelocityFieldJob() { dim = dimensions, N = N };
                wirteVekocityJob.UpdateParams(ref exchange_VelocityX.front, ref exchange_VelocityY.front, ref exchange_VelocityZ.front,
                                              in m_windFieldOri, ref forceFields);


                dep = JobHandle.CombineDependencies(writeDentsityJob.ScheduleByRef(Length, dimensions.x, dep),
                                                    wirteVekocityJob.ScheduleByRef(Length, dimensions.x, dep));
            }

            return dep;
        }
        
        [GenerateTestsForBurstCompatibility]
        public JobHandle Simulate(JobHandle dep, float deltaTime)
        {
            if (IsCreated)
            {
                dep = VelocityStep(dep, deltaTime);
                dep = DensityStep(dep, deltaTime);
            }
            return dep;
        }

        [GenerateTestsForBurstCompatibility]
        public JobHandle OptimizedSimlate(JobHandle dep, float deltaTime)
        {
            if (IsCreated)
            {
                dep = VelocityStep(dep, deltaTime);
                
                var densityDiffuse = DensityField.AddSource(dep, ref exchange_Density.front, deltaTime);

                densityDiffuse = DensityField.Diffuse(densityDiffuse, diffusion, deltaTime, iterationsCountPreFrame, convolutionMethod);
                
                dep = JobHandle.CombineDependencies(dep, densityDiffuse);
                
                //密度平流之前的部分不需要等速度迭代完
                DensityField.Swap();
                dep = DensityField.Advect(dep, ref VelocityX.back, ref VelocityY.back, ref VelocityZ.back, deltaTime);
            }

            return dep;
        }

        public JobHandle SaveToBack(JobHandle dep)
        {
            dep = JobHandle.CombineDependencies(VelocityX.Save(dep, ref exchange_VelocityX.back),
                                                VelocityY.Save(dep, ref exchange_VelocityY.back),
                                                VelocityZ.Save(dep, ref exchange_VelocityZ.back));
            
            dep = DensityField.Save(dep, ref exchange_Density.back);

            _simlateJobHandle = JobHandle.CombineDependencies(_simlateJobHandle, dep);

            return _simlateJobHandle;
        }
        

        [GenerateTestsForBurstCompatibility]
        private JobHandle DensityStep(JobHandle dep, float deltaTime)
        {
            dep = DensityField.AddSource(dep, ref exchange_Density.front, deltaTime);

            dep = DensityField.Diffuse(dep, diffusion, deltaTime, iterationsCountPreFrame, convolutionMethod);
            
            DensityField.Swap();
            dep = DensityField.Advect(dep, ref VelocityX.back, ref VelocityY.back, ref VelocityZ.back, deltaTime);

            
            return dep;
        }

        [GenerateTestsForBurstCompatibility]
        private JobHandle VelocityStep(JobHandle dep, float deltaTime)
        {
            var addSourceX = VelocityX.AddSource(dep, ref exchange_VelocityX.front, deltaTime);
            var addSourceY = VelocityY.AddSource(dep, ref exchange_VelocityY.front, deltaTime);
            var addSourceZ = VelocityZ.AddSource(dep, ref exchange_VelocityZ.front, deltaTime);
            
            var diffuseXJobHandle = VelocityX.Diffuse(addSourceX, vicosity, deltaTime, iterationsCountPreFrame, convolutionMethod);
            var diffuseYJobHandle = VelocityY.Diffuse(addSourceY, vicosity, deltaTime, iterationsCountPreFrame, convolutionMethod);
            var diffuseZJobHandle = VelocityZ.Diffuse(addSourceZ, vicosity, deltaTime, iterationsCountPreFrame, convolutionMethod);

            // dep = JobHandle.CombineDependencies(addSourceX, addSourceY, addSourceZ);
            
            dep = JobHandle.CombineDependencies(diffuseXJobHandle, diffuseYJobHandle, diffuseZJobHandle);
            
            
            dep = VelocityProject(dep, iterationsCountPreFrame);
            
            
            VelocityX.Swap(); VelocityY.Swap(); VelocityZ.Swap();
            var advectXJobHandle = VelocityX.Advect(dep, ref VelocityX.front, ref VelocityY.front, ref VelocityZ.front, deltaTime);
            var advectYJobHandle = VelocityY.Advect(dep, ref VelocityX.front, ref VelocityY.front, ref VelocityZ.front, deltaTime);
            var advectZJobHandle = VelocityZ.Advect(dep, ref VelocityX.front, ref VelocityY.front, ref VelocityZ.front, deltaTime);
            
            dep = JobHandle.CombineDependencies(advectXJobHandle, advectYJobHandle, advectZJobHandle);
            
            
            dep = VelocityProject(dep, iterationsCountPreFrame);
            
            return dep;
        }

        [GenerateTestsForBurstCompatibility]
        JobHandle VelocityProject(JobHandle dep, int iterateCount = 10)
        {
            //divergence
            ref NativeArray<float> pressure   = ref VelocityX.front,//
                       divergence = ref VelocityZ.front;
            
            var _divergenceJob = new DivergenceJob() { dim = dimensions, N = N, };
            
            _divergenceJob.UpdateParams(ref pressure, ref divergence,
                                        ref VelocityX.back, ref VelocityY.back, ref VelocityZ.back);

            var divergenceJobHandle = _divergenceJob.ScheduleByRef(NSize, IteratorX, dep);
            var setBndDivJobHandle  = VelocityX.SetBoundaryClamp(divergenceJobHandle, ref pressure, 0);
            var setBndPJobHandle    = VelocityY.SetBoundaryClamp(divergenceJobHandle, ref divergence, 0);
            
            dep = JobHandle.CombineDependencies(setBndDivJobHandle, setBndPJobHandle);
            
            
            //pressure
            var _pressureJob = new PressureJob(){ dim = dimensions, N = N, };
            
            _pressureJob.UpdateParams(ref pressure, ref divergence);

            for (int i = 0; i < iterateCount; i++)
            {
                dep = _pressureJob.ScheduleByRef(NSize, IteratorX, dep);
                dep = VelocityX.SetBoundaryClamp(dep, ref pressure, 0);
            }
            
            //update Velocity
            var _correctVelocityJob = new CorrectVelocityJob(){ dim = dimensions, N = N, };
            
            _correctVelocityJob.UpdateParams(ref pressure, ref VelocityX.back, ref VelocityY.back, ref VelocityZ.back);
            dep = _correctVelocityJob.ScheduleByRef(NSize, IteratorX ,dep);

            return JobHandle.CombineDependencies(VelocityX.SetBoundaryClampBack(dep),
                                                 VelocityY.SetBoundaryClampBack(dep),
                                                 VelocityZ.SetBoundaryClampBack(dep));
        }


        private void Start()
        {
            Initialize(gridSize, diffusion, vicosity, Allocator.Persistent,
                       windFieldCenter);
        }


        [Space(), Header("windField"), Range(0,1)] 
        public float diffusion = 0.5f;
        
        [Range(0,1)]
        public float vicosity = 0.5f;

        [Range(1,20), Tooltip("约等于耗散次数，慎重")]
        public int iterationsCountPreFrame = 5;
        
        [_ExponentialSlider(1e-10f, 1e-1f, 4f, true),Tooltip("激活静态叶子的阈值")]
        public float activeVelocityThreshold = 5e-3f;

        public Transform windFieldCenter;

        [_ReadOnlyInPlayMode] public int3 gridSize = new int3(16, 8, 16);
        
        public  Vector3 centerOffset = new Vector3(0.5f, 0.5f, 0.5f);

        public ConvolutionMethod convolutionMethod = ConvolutionMethod.LineSequence;
        
        public bool drawPos;

        public bool drawDir;

        public bool drawBounds;

        private bool DrawDebug => drawPos || drawDir || drawBounds;
        private void OnDrawGizmos()
        {
            if(Application.isPlaying && IsCreated)
                DrawWindField( dimensions);
        }

#if UNITY_EDITOR

        public void DrawWindField(int3 size)
        {
            if(!DrawDebug)
                return;

            _simlateJobHandle.Complete();
            
            ref NativeArray<float> D = ref exchange_Density.back,
                                   X = ref exchange_VelocityX.back,
                                   Z = ref exchange_VelocityZ.back,
                                   Y = ref exchange_VelocityY.back;

            float3    ori    = m_windFieldOri;

            Gizmos.color = Color.green;
            if (drawPos)
                for (int y = 0; y < size.y; y++)
                {
                    for (int z = 0; z < size.z; z++)
                    {
                        for (int x = 0; x < size.x; x++)
                        {
                            float3 pos   = (new float3(x, y, z) + ori);

                            Gizmos.DrawWireSphere(pos, 0.02f);
                        }
                    }
                }
            
            if (drawDir)
                for (int y = 0; y < size.y; y++)
                {
                    for (int z = 0; z < size.z; z++)
                    {
                        for (int x = 0; x < size.x; x++)
                        {
                            int    index = GridUtils.GetIndex(x, y, z, size);
                            float3 pos   = (new float3(x, y, z) + ori);

                            float3 dir = new float3(X[index], Y[index], Z[index]);

                            if (math.any(math.isnan(dir)))
                            {
                                Gizmos.color = Color.red;
                                Gizmos.DrawLine(pos, pos + MathematicsUtil.up * 0.2f);
                            }
                            else
                            {
                                Gizmos.color = Color.green;
                                Gizmos.DrawLine(pos, pos + dir);
                            }
                        }
                    }
                }
            // Gizmos.color = Color.green;
            PBDBounds bounds = WindFieldBounds;
            float3    center = bounds.Center;
            
            if(drawBounds)
            {
                Gizmos.DrawLine(ori, center);
                Gizmos.DrawWireCube(center, (float3)dimensions);
            }
        }
        
#endif
    }
}