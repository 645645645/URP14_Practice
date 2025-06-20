using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace UnityEngine.PBD
{
    public enum BoundaryType
    {
        Density = 0,
        VelocityX = 1,
        VelocityY = 2,
        VelocityZ = 3,
    }

    [GenerateTestsForBurstCompatibility]
    public class DoubleBuffer : IDisposable
    {
        private NativeArray<float> dataA;

        private NativeArray<float> dataB;

        private bool m_AisBackBuffer;

        private readonly int3 dimensions;
        private readonly int3 N;

        private readonly BoundaryType m_boundaryType;

        public int Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => dimensions.x * dimensions.y * dimensions.z;
        }


        public int BatchCountX => dimensions.x;

        public int  NSize => N.x * N.y * N.z;
        
        public int IteratorX => N.x;
        public int IteratorY => N.y;
        public int IteratorZ => N.z;

        private AddSourceJob   _addSourceJob;
        private DiffuseLineJob _diffuseLineJob;
        private DiffuseRBGSJob _diffuseRGBS;
        private AdvectJob      _advectJob;

        private SetBoundaryParallelForJob _setBoundaryParallelForJob;

        private WindSimulateSaveJob _saveJob;
        
        public bool IsCreated
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => dataA.IsCreated && dataB.IsCreated;
        }

        public DoubleBuffer(int3 dims, Allocator allocator, BoundaryType type = BoundaryType.Density)
        {
            dimensions      = dims;
            m_AisBackBuffer = true;
            m_boundaryType  = type;
            N               = dims - 2;

            int length = dimensions.x * dimensions.y * dimensions.z;

            int size = UnsafeUtility.SizeOf<float>() * length;

            dataA = new NativeArray<float>(length, allocator);
            dataB = new NativeArray<float>(length, allocator);
            
            int4 stride = GridUtils.GetStride(dimensions);

            _addSourceJob   = new AddSourceJob() { };
            _diffuseLineJob = new DiffuseLineJob() { dim = dimensions, N = N, stride   = stride };
            _diffuseRGBS    = new DiffuseRBGSJob() { dim = dimensions, N = N, stride   = stride };
            _advectJob      = new AdvectJob() { dim      = dimensions, N = N, stride = stride, maxRange = (float3)dims - 0.5f};

            _setBoundaryParallelForJob = new SetBoundaryParallelForJob() { dim = dimensions, };

            _saveJob = new WindSimulateSaveJob() { size = size, };
        }

        public void Swap()
        {
            m_AisBackBuffer = !m_AisBackBuffer;
        }

        private void Swap(ref NativeArray<float> A, ref NativeArray<float> B)
        {
            (A, B) = (B, A);
        }
        
        
        public ref NativeArray<float> back =>  ref m_AisBackBuffer ? ref dataA : ref dataB; 
        

        public  ref NativeArray<float> front=>  ref m_AisBackBuffer ?  ref dataB : ref  dataA;

        public void Dispose()
        {
            if (IsCreated)
            {
                dataA.Dispose();
                dataB.Dispose();
            }
        }
        
        public void ExchangeBufferWithFront(ref NativeArray<float> source)
        {
            Swap(ref front, ref source);
        }

        public JobHandle AddSource(JobHandle dep, ref NativeArray<float> source, float deltaTime,
                                   ParallelUnit parallelUnit = ParallelUnit.Line)
        {
            ExchangeBufferWithFront(ref source);

            _addSourceJob.UpdateParams(ref back, ref front, deltaTime);

            return parallelUnit switch
                   {
                       ParallelUnit.Line => _addSourceJob.ScheduleByRef(Length, BatchCountX, dep),
                       _                 => _addSourceJob.ScheduleByRef(dep),
                   };
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="dep"></param>
        /// <param name="ratio">密度场为Diffusion，速度场为viscosty</param>
        /// <param name="deltaTime"></param>
        /// <param name="iterateCount"></param>
        /// <param name="parallelUnit"></param>
        /// <param name="convolution"></param>
        /// <returns></returns>
        public JobHandle Diffuse(JobHandle dep, float ratio, float deltaTime, int iterateCount = 10, 
                                 ParallelUnit parallelUnit = ParallelUnit.Line,
                                 ConvolutionMethod convolution = ConvolutionMethod.LineSlidingWindow)
        {
            Swap();

            float a = deltaTime * ratio * (N.x * N.y * N.z);

            switch (convolution)
            {
                case ConvolutionMethod.LineSequence:
                case ConvolutionMethod.LineSlidingWindow:
                {
                    float  div              = 1 / math.mad(2, a, 1);
                    float  absorption       = a * div;
                    float2 DivAndAbsorption = new float2(div, absorption);

                    _diffuseLineJob.UpdateParams(ref back, ref front, DivAndAbsorption, convolution,
                                                 iterateCount:iterateCount, (int)m_boundaryType);
                    
                    if (parallelUnit == ParallelUnit.Line)
                    {
                        for (int i = 0; i < iterateCount; i++)
                        {
                            _diffuseLineJob.iterateDirection = 0;

                            dep = _diffuseLineJob.ScheduleByRef(NSize, IteratorX, dep);

                            _diffuseLineJob.iterateDirection = 2;

                            dep = _diffuseLineJob.ScheduleByRef(NSize, IteratorZ, dep);

                            _diffuseLineJob.iterateDirection = 1;

                            dep = _diffuseLineJob.ScheduleByRef(NSize, IteratorY, dep);

                            dep = SetBoundaryClampBack(dep);
                        }
                    }
                    else
                    {
                        // for (int i = 0; i < iterateCount; i++)
                        // {
                        //     
                        // }

                        dep = _diffuseLineJob.ScheduleByRef(dep);
                    }
                }
                    break;

                case ConvolutionMethod.RBGSeidel:
                {
                    float  div              = 1 / math.mad(6, a, 1);
                    float  absorption       = a * div;
                    float2 DivAndAbsorption = new float2(div, absorption);
                    
                    _diffuseRGBS.UpdateParams(ref back, ref front, DivAndAbsorption, iterateCount:iterateCount, (int)m_boundaryType);

                    if (parallelUnit == ParallelUnit.Line)
                    {
                        for (int i = 0; i < iterateCount; i++)
                        {
                            _diffuseRGBS.IsRedPhase = true;

                            dep = _diffuseRGBS.ScheduleByRef(NSize, IteratorX, dep);

                            _diffuseRGBS.IsRedPhase = false;

                            dep = _diffuseRGBS.ScheduleByRef(NSize, IteratorX, dep);

                            dep = SetBoundaryClampBack(dep);
                        }
                    }
                    else
                    {
                        // for (int i = 0; i < iterateCount; i++)
                        // {
                        //     
                        // }

                        dep = _diffuseRGBS.ScheduleByRef(dep);
                    }
                }
                    break;
            }

            return dep;
        }

        public JobHandle SetBoundaryClamp(JobHandle dep, ref NativeArray<float> data, int b)
        {
            _setBoundaryParallelForJob.UpdateParams(ref data, b);
            dep = _setBoundaryParallelForJob.ScheduleByRef(6, 1, dep);
            return dep;
        }

        public JobHandle SetBoundaryClampBack(JobHandle dep)
        {
            return SetBoundaryClamp(dep, ref back, (int)m_boundaryType);
        }

        //density  速度合并更新了不走这
        public JobHandle Advect(JobHandle    dep, ref NativeArray<float> u, ref NativeArray<float> v, ref NativeArray<float> w, float deltaTime, 
                                ParallelUnit parallelUnit = ParallelUnit.Line)
        {
            Swap();

            _advectJob.UpdateParams(ref back, ref front, ref u, ref v, ref w, deltaTime);

            if (parallelUnit == ParallelUnit.Line)
            {
                dep = _advectJob.ScheduleByRef(NSize, IteratorX, dep);

                dep = SetBoundaryClampBack(dep);
            }
            else
            {
                dep = _advectJob.ScheduleByRef(dep);
            }
            return dep;
        }


        public JobHandle Save(JobHandle dep, ref NativeArray<float> data)
        {
            _saveJob.UpdateParams(ref back, ref data);
            
            return _saveJob.ScheduleByRef(dep);
        }

    }
}