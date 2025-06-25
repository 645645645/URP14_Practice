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
    public class DoubleBuffer : DoubleBufferBase<float>
    {
        private AddSourceJob   _addSourceJob;
        private DiffuseLineJob _diffuseLineJob;
        private DiffuseRBGSJob _diffuseRGBS;
        

        private ForwardAdvectDensityJob _forwardAdvectDensityJob;
        private ReverseAdvectDensityJob _reverseAdvectDensityJob;

        private SetBoundaryJob _setBoundaryJob;

        private WindSimulateSaveJob _saveJob;

        private WindFieldClearDataJob<float> _windFieldClearJob;

        public DoubleBuffer(int3 dims, Allocator allocator, BoundaryType type = BoundaryType.Density) : base(dims, allocator, type) 
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
            _diffuseLineJob = new DiffuseLineJob() { dim = dimensions, N = N, stride = stride };
            _diffuseRGBS    = new DiffuseRBGSJob() { dim = dimensions, N = N, stride = stride };

            _forwardAdvectDensityJob = new ForwardAdvectDensityJob()
            {
                dim      = dimensions,
                N        = N,
                stride   = stride,
                maxRange = (float3)dims - 1.5f
            };
            
            _reverseAdvectDensityJob= new ReverseAdvectDensityJob()
            {
                dim      = dimensions,
                N        = N,
                stride   = stride,
                maxRange = (float3)dims - 1.5f
            };

            _setBoundaryJob = new SetBoundaryJob() { dim = dimensions, N = N, stride = stride };

            _saveJob = new WindSimulateSaveJob() { size = size, };
            
            _windFieldClearJob = new WindFieldClearDataJob<float>();
        }

        public override JobHandle AddSource(JobHandle dep, ref NativeArray<float> source, float deltaTime,
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

        public override JobHandle Diffuse(JobHandle dep, float ratio, float deltaTime, int iterateCount = 10, 
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

        public override JobHandle SetBoundaryClamp(JobHandle dep, ref NativeArray<float> data, int b)
        {
            _setBoundaryJob.UpdateParams(ref data, b);
            // dep = _setBoundaryJob.ScheduleByRef(6, 1, dep);
            dep = _setBoundaryJob.ScheduleByRef(dep);
            return dep;
        }
        
        
        public override JobHandle ClearBack(JobHandle dep)
        {
            _windFieldClearJob.UpdateParams(ref back);
            return _windFieldClearJob.ScheduleByRef(dep);
        }

        public override JobHandle ClearData(JobHandle dep, ref NativeArray<float> data)
        {
            _windFieldClearJob.UpdateParams(ref data);
            return _windFieldClearJob.ScheduleByRef(dep);
        }

        //density  速度合并更新了不走这
        public override JobHandle Advect(JobHandle    dep,                ref NativeArray<float> u, ref NativeArray<float> v, ref NativeArray<float> w,
                                         float        forwardAdvectRatio, float                  deltaTime,
                                         ParallelUnit parallelUnit = ParallelUnit.Line)
        {
            Swap();

            _forwardAdvectDensityJob.UpdateParams(ref back, ref front, ref u, ref v, ref w, deltaTime, forwardAdvectRatio);
            _reverseAdvectDensityJob.UpdateParams(ref back, ref front, ref u, ref v, ref w, deltaTime, 1 - forwardAdvectRatio);

            JobHandle reverseAdvect = default,
                      forwardAdvect = default;

            if (parallelUnit == ParallelUnit.Line)
            {
                if (forwardAdvectRatio < 0.9f)
                    reverseAdvect = _reverseAdvectDensityJob.ScheduleByRef(NSize, IteratorX, dep);
                
                if (forwardAdvectRatio > 0.1f)
                    forwardAdvect = _forwardAdvectDensityJob.ScheduleByRef(NSize, IteratorX, dep);
            }
            else
            {
                if (forwardAdvectRatio < 0.9f)
                    reverseAdvect = _reverseAdvectDensityJob.ScheduleByRef(dep);
                
                if (forwardAdvectRatio > 0.1f)
                    forwardAdvect = _forwardAdvectDensityJob.ScheduleByRef(dep);
                
            }
            
            dep = JobHandle.CombineDependencies(reverseAdvect, forwardAdvect);
            dep = SetBoundaryClampBack(dep);
            return dep;
        }


        public override JobHandle Save(JobHandle dep, ref NativeArray<float> data)
        {
            _saveJob.UpdateParams(ref back, ref data);
            
            return _saveJob.ScheduleByRef(dep);
        }

    }
}