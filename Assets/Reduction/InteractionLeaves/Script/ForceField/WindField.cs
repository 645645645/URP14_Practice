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

    public enum DataFormat
    {
        FloatPoint,
        FixedPoint,
    }
    
    public class WindField : MonoBehaviour
    {

        private FloatPointField _floatPointField;
        private FixedPointFiled _fixedPointFiled;
        
        private int3 dimensions;
        private int3 N;

        public int3 Dim     => dimensions;
        public int3 PublicN => N;

        public ref NativeArray<float> Exchange_Density
        {
            get
            {
                if (dataFormat == DataFormat.FloatPoint)
                    return ref _floatPointField.Exchange_Density;
                return ref _fixedPointFiled.Exchange_Density;
            }
        }
        
        public ref NativeArray<float> Exchange_VelocityX
        {
            get
            {
                if (dataFormat == DataFormat.FloatPoint)
                    return ref _floatPointField.Exchange_VelocityX;
                return ref _fixedPointFiled.Exchange_VelocityX;
            }
        }

        public ref NativeArray<float> Exchange_VelocityY
        {
            get
            {
                if (dataFormat == DataFormat.FloatPoint)
                    return ref _floatPointField.Exchange_VelocityY;
                return ref _fixedPointFiled.Exchange_VelocityY;
            }
        }

        public ref NativeArray<float> Exchange_VelocityZ
        {
            get
            {
                if (dataFormat == DataFormat.FloatPoint)
                    return ref _floatPointField.Exchange_VelocityZ;
                return ref _fixedPointFiled.Exchange_VelocityZ;
            }
        }
        
        public bool IsCreated
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (dataFormat == DataFormat.FloatPoint && _floatPointField.IsCreated) ||
                   (dataFormat == DataFormat.FixedPoint && _fixedPointFiled.IsCreated);
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
        
        public void Initialize()
        {
            (int3 dims, Allocator allocator, Transform center) =
                (gridSize, Allocator.Persistent, windFieldCenter);
            
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

            int4 stride = GridUtils.GetStride(dims);

            m_windFieldCenter = center ? center : transform;

            m_lasFramePosition = m_windFieldCenter.position;

            switch (dataFormat)
            {
                case DataFormat.FloatPoint:
                    _floatPointField = new FloatPointField();
                    _floatPointField.Initialize(dims, N, stride, allocator, overSanpling);
                    break;

                case DataFormat.FixedPoint:
                    _fixedPointFiled = new FixedPointFiled();
                    _fixedPointFiled.Initialize(dims, N, stride, allocator, overSanpling);
                    break;
            }
        }

        public void Dispose()
        {
            _simlateJobHandle.Complete();
            _fixedPointFiled?.Dispose();
            _floatPointField?.Dispose();
        }

        public void UpdateFieldParams()
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

                dep = dataFormat switch
                      {
                          DataFormat.FloatPoint => _floatPointField.WriteToFrontBuffer(dep, ref forceFields, ref colliders,
                                                                                       in m_windFieldOri, in m_windFieldBounds, useDensityField, overSanpling),
                          
                          _ => _fixedPointFiled.WriteToFrontBuffer(dep, ref forceFields, ref colliders,
                                                                   in m_windFieldOri, in m_windFieldBounds, useDensityField, overSanpling),
                      };
            }

            return dep;
        }


        
        [GenerateTestsForBurstCompatibility]
        public JobHandle Simulate(JobHandle dep, float deltaTime)
        {
            if (IsCreated)
            {
                switch (dataFormat)
                {
                    case DataFormat.FloatPoint:
                        dep = _floatPointField.Simulate(dep, deltaTime, vicosity,
                                                                diffusion, forwardAdvectRatio,
                                                                iterationsCountPreFrame, useDensityField, 
                                                                parallelUnit, convolutionMethod);
                        break;
                    case DataFormat.FixedPoint:
                        dep = _fixedPointFiled.Simulate(dep, new VInt(deltaTime),new VInt(vicosity), 
                                                                new VInt(diffusion), new VInt(forwardAdvectRatio), 
                                                                iterationsCountPreFrame, useDensityField, 
                                                                parallelUnit, convolutionMethod);
                        break;
                }
            }
            return dep;
        }

        public JobHandle SaveToBack(JobHandle dep)
        {
            _simlateJobHandle = dataFormat switch
                                {
                                    DataFormat.FloatPoint => _simlateJobHandle = _floatPointField.SaveToBack(dep, useDensityField),

                                    _ => _simlateJobHandle = _fixedPointFiled.SaveToBack(dep, useDensityField),
                                };

            return _simlateJobHandle;
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

        [_ReadOnlyInPlayMode] public DataFormat dataFormat = DataFormat.FloatPoint;

        public ParallelUnit parallelUnit = ParallelUnit.Line;

        public ConvolutionMethod convolutionMethod = ConvolutionMethod.LineSequence;

        public bool useDensityField = false;

        [_ReadOnlyInPlayMode] public bool overSanpling = true;

        [Range(0, 1)] public float forwardAdvectRatio = 0f;

#if UNITY_EDITOR
        
        [Space, Header("DebugDraw")]
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
            
            ref NativeArray<float> D = ref Exchange_Density,
                                   X = ref Exchange_VelocityX,
                                   Z = ref Exchange_VelocityZ,
                                   Y = ref Exchange_VelocityY;

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