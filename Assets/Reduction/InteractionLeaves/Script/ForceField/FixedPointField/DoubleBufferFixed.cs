using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace UnityEngine.PBD
{
    
    [GenerateTestsForBurstCompatibility]
    public class DoubleBufferFixed : DoubleBufferBase<VInt>
    {
        private AddSourceJobFixed   _addSourceJob;
        private DiffuseLineJobFixed _diffuseLineJob;
        private DiffuseRBGSJobFixed _diffuseRGBS;

        private ForwardAdvectDensityJobFixed _forwardAdvectDensityJob;
        private ReverseAdvectDensityJobFixed _reverseAdvectDensityJob;

        private SetBoundaryJobFixed _setBoundaryJob;

        private WindSimulateSaveJobFixed _saveJob;

        private WindFieldClearDataJob<VInt> _windFieldClearJob;

        public DoubleBufferFixed(int3 dims, Allocator allocator, BoundaryType type = BoundaryType.Density) : base(dims, allocator, type)
        {
            dimensions      = dims;
            m_AisBackBuffer = true;
            m_boundaryType  = type;
            N               = dims - 2;

            int length = dimensions.x * dimensions.y * dimensions.z;

            int size = UnsafeUtility.SizeOf<VInt>() * length;

            dataA = new NativeArray<VInt>(length, allocator);
            dataB = new NativeArray<VInt>(length, allocator);
            
            int4 stride = GridUtils.GetStride(dimensions);

            _addSourceJob            = new AddSourceJobFixed() { };
            _diffuseLineJob          = new DiffuseLineJobFixed() { dim = dimensions, N = N, stride = stride };
            _diffuseRGBS             = new DiffuseRBGSJobFixed() { dim = dimensions, N = N, stride = stride };

            _forwardAdvectDensityJob = new ForwardAdvectDensityJobFixed()
            {
                dim      = dimensions,
                N        = N,
                stride   = stride,
                maxRange = new VInt3((float3)dims - 1.5f)
            };
            
            _reverseAdvectDensityJob= new ReverseAdvectDensityJobFixed()
            {
                dim      = dimensions,
                N        = N,
                stride   = stride,
                maxRange = new VInt3((float3)dims - 1.5f)
            };

            _setBoundaryJob = new SetBoundaryJobFixed() { dim = dimensions,N = N, stride = stride};

            _saveJob = new WindSimulateSaveJobFixed() { size = size, };

            _windFieldClearJob = new WindFieldClearDataJob<VInt>();
        }

        public override JobHandle AddSource(JobHandle    dep, ref NativeArray<VInt> source, VInt deltaTime,
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

        public override JobHandle Diffuse(JobHandle dep, VInt ratio, VInt deltaTime, int iterateCount = 10, 
                                 ParallelUnit       parallelUnit = ParallelUnit.Line,
                                 ConvolutionMethod  convolution  = ConvolutionMethod.LineSlidingWindow)
        {
            Swap();

            VInt a = deltaTime * ratio * (N.x * N.y * N.z);

            switch (convolution)
            {
                case ConvolutionMethod.LineSequence:
                case ConvolutionMethod.LineSlidingWindow:
                {
                    VInt div = 1 / (a * 2 - 1);

                    VInt absorption = a * div;

                    VInt2 DivAndAbsorption = new VInt2(div, absorption);

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
                    VInt div = 1 / (a * 6 - 1);

                    VInt absorption = a * div;

                    VInt2 DivAndAbsorption = new VInt2(div, absorption);
                    
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

        public override JobHandle SetBoundaryClamp(JobHandle dep, ref NativeArray<VInt> data, int b)
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

        public override JobHandle ClearData(JobHandle dep, ref NativeArray<VInt> data)
        {
            _windFieldClearJob.UpdateParams(ref data);
            return _windFieldClearJob.ScheduleByRef(dep);
        }

        //density  速度合并更新了不走这
        public override JobHandle Advect(JobHandle    dep,                ref NativeArray<VInt> u, ref NativeArray<VInt> v, ref NativeArray<VInt> w,
                                         VInt         forwardAdvectRatio, VInt                  deltaTime,
                                         ParallelUnit parallelUnit = ParallelUnit.Line)
        {
            Swap();

            _forwardAdvectDensityJob.UpdateParams(ref back, ref front, ref u, ref v, ref w, deltaTime, forwardAdvectRatio);
            _reverseAdvectDensityJob.UpdateParams(ref back, ref front, ref u, ref v, ref w, deltaTime, FixedPointUtils.ScaleInv - forwardAdvectRatio);

            JobHandle reverseAdvect = default,
                      forwardAdvect = default;
            
            if (parallelUnit == ParallelUnit.Line)
            {
                if (forwardAdvectRatio < new VInt(0.9f))
                    reverseAdvect = _reverseAdvectDensityJob.ScheduleByRef(NSize, IteratorX, dep);
                
                if (forwardAdvectRatio > new VInt(0.1f))
                    forwardAdvect = _forwardAdvectDensityJob.ScheduleByRef(NSize, IteratorX, dep);
            }
            else
            {
                if (forwardAdvectRatio < new VInt(0.9f))
                    reverseAdvect = _reverseAdvectDensityJob.ScheduleByRef(dep);
                
                if (forwardAdvectRatio > new VInt(0.1f))
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