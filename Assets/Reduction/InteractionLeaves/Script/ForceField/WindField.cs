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
        RBGSeidel,
    }

    public enum ParallelUnit
    {
        Line,
        IJob,
    }
    
    public class WindField : MonoBehaviour
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

        private AdvectVelocityJob  _advectVelocityJob;
        private DivergenceJob      _divergenceJob;
        private PressureJob        _pressureJob;
        private CorrectVelocityJob _correctVelocityJob;

        private WriteDensityFieldJob  _writeDentsityJob;
        private WriteVelocityFieldJob _wirteVelocityJob;
        
        public void Initialize(int3      dims,
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

            int length = dims.x * dims.y * dims.z;
            
            exchange_Density   = new WindFiledExchangeBuffer(length, allocator);
            exchange_VelocityX = new WindFiledExchangeBuffer(length, allocator);
            exchange_VelocityY = new WindFiledExchangeBuffer(length, allocator);
            exchange_VelocityZ = new WindFiledExchangeBuffer(length, allocator);

            m_windFieldCenter = center ? center : transform;

            m_lasFramePosition = m_windFieldCenter.position;

            int4 stride = GridUtils.GetStride(dims);

            _advectVelocityJob = new AdvectVelocityJob() { 
                dim      = dims, 
                N        = N,
                stride   = stride,
                maxRange = (float3)dims - 0.5f 
            };

            _divergenceJob      = new DivergenceJob() { dim      = dims, N = N, stride = stride };
            _pressureJob        = new PressureJob() { dim        = dims, N = N, stride = stride };
            _correctVelocityJob = new CorrectVelocityJob() { dim = dims, N = N, stride = stride };

            _writeDentsityJob = new WriteDensityFieldJob() { dim  = dims, N = N };
            _wirteVelocityJob = new WriteVelocityFieldJob() { dim = dims, N = N };
        }

        public void Dispose()
        {
            DensityField.Dispose();
            VelocityX.Dispose();
            VelocityY.Dispose();
            VelocityZ.Dispose();
            exchange_Density.Dispose();
            exchange_VelocityX.Dispose();
            exchange_VelocityY.Dispose();
            exchange_VelocityZ.Dispose();
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

                _wirteVelocityJob.UpdateParams(ref exchange_VelocityX.front, ref exchange_VelocityY.front, ref exchange_VelocityZ.front,
                                               in m_windFieldOri, ref forceFields);
                if (useDensityField)
                {
                    _writeDentsityJob.UpdateParams(ref exchange_Density.front, in m_windFieldOri, ref colliders);

                    dep = parallelUnit switch
                          {
                              ParallelUnit.Line => JobHandle.CombineDependencies(_writeDentsityJob.ScheduleByRef(Length, dimensions.x, dep),
                                                                                 _wirteVelocityJob.ScheduleByRef(Length, dimensions.x, dep)),
                              // ParallelUnit.IJob
                              _ => JobHandle.CombineDependencies(_writeDentsityJob.ScheduleByRef(dep),
                                                                 _wirteVelocityJob.ScheduleByRef(dep)),
                          };
                }
                else
                {
                    dep = parallelUnit switch
                          {
                              ParallelUnit.Line => _wirteVelocityJob.ScheduleByRef(Length, dimensions.x, dep),
                              // ParallelUnit.IJob
                              _ => _wirteVelocityJob.ScheduleByRef(dep),
                          };
                }
            }

            return dep;
        }
        
        [GenerateTestsForBurstCompatibility]
        public JobHandle SimulateParallel(JobHandle dep, float deltaTime)
        {
            if (IsCreated)
            {
                dep = VelocityStepParallel(dep, deltaTime);
                if(useDensityField)
                    dep = DensityStepParallel(dep, deltaTime);
            }
            return dep;
        }

        [GenerateTestsForBurstCompatibility]
        public JobHandle OptimizedSimulateParallel(JobHandle dep, float deltaTime)
        {
            if (IsCreated)
            {
                var velocityStep = VelocityStepParallel(dep, deltaTime);

                if (useDensityField)
                {
                    var densityDiffuse = DensityField.AddSource(dep, ref exchange_Density.front, deltaTime, parallelUnit);

                    densityDiffuse = DensityField.Diffuse(densityDiffuse, diffusion, deltaTime, iterationsCountPreFrame,
                                                          parallelUnit, convolutionMethod);

                    dep = JobHandle.CombineDependencies(velocityStep, densityDiffuse);

                    //密度平流之前的部分不需要等速度迭代完
                    dep = DensityField.Advect(dep, ref VelocityX.back, ref VelocityY.back, ref VelocityZ.back, deltaTime, parallelUnit);
                }
                else
                {
                    dep = velocityStep;
                }
            }

            return dep;
        }

        public JobHandle SaveToBack(JobHandle dep)
        {
            _simlateJobHandle = JobHandle.CombineDependencies(VelocityX.Save(dep, ref exchange_VelocityX.back),
                                                              VelocityY.Save(dep, ref exchange_VelocityY.back),
                                                              VelocityZ.Save(dep, ref exchange_VelocityZ.back));
            if (useDensityField)
            {
                dep = DensityField.Save(dep, ref exchange_Density.back);

                _simlateJobHandle = JobHandle.CombineDependencies(_simlateJobHandle, dep);
            }

            return _simlateJobHandle;
        }
        

        [GenerateTestsForBurstCompatibility]
        private JobHandle DensityStepParallel(JobHandle dep, float deltaTime)
        {
            dep = DensityField.AddSource(dep, ref exchange_Density.front, deltaTime, parallelUnit);

            dep = DensityField.Diffuse(dep, diffusion, deltaTime, iterationsCountPreFrame,
                                       parallelUnit, convolutionMethod);
            
            dep = DensityField.Advect(dep, ref VelocityX.back, ref VelocityY.back, ref VelocityZ.back, deltaTime, parallelUnit);

            
            return dep;
        }

        [GenerateTestsForBurstCompatibility]
        private JobHandle VelocityStepParallel(JobHandle dep, float deltaTime)
        {
            var addSourceX = VelocityX.AddSource(dep, ref exchange_VelocityX.front, deltaTime, parallelUnit);
            var addSourceY = VelocityY.AddSource(dep, ref exchange_VelocityY.front, deltaTime, parallelUnit);
            var addSourceZ = VelocityZ.AddSource(dep, ref exchange_VelocityZ.front, deltaTime, parallelUnit);

            var diffuseXJobHandle = VelocityX.Diffuse(addSourceX, vicosity, deltaTime, iterationsCountPreFrame,
                                                      parallelUnit, convolutionMethod);
            var diffuseYJobHandle = VelocityY.Diffuse(addSourceY, vicosity, deltaTime, iterationsCountPreFrame,
                                                      parallelUnit, convolutionMethod);
            var diffuseZJobHandle = VelocityZ.Diffuse(addSourceZ, vicosity, deltaTime, iterationsCountPreFrame,
                                                      parallelUnit, convolutionMethod);

            // dep = JobHandle.CombineDependencies(addSourceX, addSourceY, addSourceZ);
            
            dep = JobHandle.CombineDependencies(diffuseXJobHandle, diffuseYJobHandle, diffuseZJobHandle);
            
            
            dep = VelocityProject(dep, iterationsCountPreFrame);
            
            
            VelocityX.Swap(); VelocityY.Swap(); VelocityZ.Swap();

            _advectVelocityJob.UpdateParams(ref VelocityX.back, ref VelocityY.back, ref VelocityZ.back,
                                            ref VelocityX.front, ref VelocityY.front, ref VelocityZ.front, deltaTime);

            if (parallelUnit == ParallelUnit.Line)
            {
                dep = _advectVelocityJob.ScheduleByRef(NSize, IteratorX, dep);
                dep = JobHandle.CombineDependencies(VelocityX.SetBoundaryClampBack(dep),
                                                    VelocityY.SetBoundaryClampBack(dep),
                                                    VelocityZ.SetBoundaryClampBack(dep));
            }
            else
            {
                dep = _advectVelocityJob.ScheduleByRef(dep);
            }

            dep = VelocityProject(dep, iterationsCountPreFrame);
            
            return dep;
        }

        [GenerateTestsForBurstCompatibility]
        JobHandle VelocityProject(JobHandle dep, int iterateCount = 10)
        {
            ref NativeArray<float> pressure   = ref VelocityX.front,//
                       divergence = ref VelocityZ.front;
            
            
            _divergenceJob.UpdateParams(ref pressure, ref divergence,
                                        ref VelocityX.back, ref VelocityY.back, ref VelocityZ.back);

            _pressureJob.UpdateParams(ref pressure, ref divergence, iterateCount);
            
            _correctVelocityJob.UpdateParams(ref pressure, ref VelocityX.back, ref VelocityY.back, ref VelocityZ.back);
            
            if (parallelUnit == ParallelUnit.Line)
            {
                //divergence
                var divergenceJobHandle = _divergenceJob.ScheduleByRef(NSize, IteratorX, dep);
                var setBndDivJobHandle  = VelocityX.SetBoundaryClamp(divergenceJobHandle, ref pressure, 0);
                var setBndPJobHandle    = VelocityY.SetBoundaryClamp(divergenceJobHandle, ref divergence, 0);

                dep = JobHandle.CombineDependencies(setBndDivJobHandle, setBndPJobHandle);
                
                //pressure
                for (int i = 0; i < iterateCount; i++)
                {
                    _pressureJob.IsRedPhase = true;

                    dep = _pressureJob.ScheduleByRef(NSize, IteratorX, dep);
                
                
                    _pressureJob.IsRedPhase = false;

                    dep = _pressureJob.ScheduleByRef(NSize, IteratorX, dep);
                
                    dep = VelocityX.SetBoundaryClamp(dep, ref pressure, 0);
                }
                
                //update Velocity
                dep = _correctVelocityJob.ScheduleByRef(NSize, IteratorX, dep);

                dep = JobHandle.CombineDependencies(VelocityX.SetBoundaryClampBack(dep),
                                                    VelocityY.SetBoundaryClampBack(dep),
                                                    VelocityZ.SetBoundaryClampBack(dep));
            }
            else
            {
                dep = _divergenceJob.ScheduleByRef(dep);
                dep = _pressureJob.ScheduleByRef(dep);
                dep = _correctVelocityJob.ScheduleByRef(dep);
            }

            return dep;
        }


        private void Start()
        {
            Initialize(gridSize, Allocator.Persistent, windFieldCenter);
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

        public ParallelUnit parallelUnit = ParallelUnit.Line;

        public ConvolutionMethod convolutionMethod = ConvolutionMethod.LineSequence;

        public bool useDensityField = false;
        

#if UNITY_EDITOR
        public bool drawPos;

        public bool drawDir;

        public bool drawBounds;

        private bool DrawDebug => drawPos || drawDir || drawBounds;
        private void OnDrawGizmos()
        {
            if(Application.isPlaying && IsCreated)
                DrawWindField( dimensions);
        }

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