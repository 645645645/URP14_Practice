using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace UnityEngine.PBD
{
    //重构(x) 复制(√)
    public class FixedPointFiled : IFPointFieldBase<VInt>
    {
        private DoubleBufferFixed DensityField;

        private DoubleBufferFixed VelocityX;
        private DoubleBufferFixed VelocityY;
        private DoubleBufferFixed VelocityZ;
        
        private ExchangeBufferFixed exchange_Density;
        private ExchangeBufferFixed exchange_VelocityX;
        private ExchangeBufferFixed exchange_VelocityY;
        private ExchangeBufferFixed exchange_VelocityZ;

        private ForwardAdvectVelocityJobFixed _forwardAdvectVelocityJob;
        private ReverseAdvectVelocityJobFixed _reverseAdvectVelocityJob;
        private DivergenceJobFixed            _divergenceJob;
        private PressureJobFixed              _pressureJob;
        private CorrectVelocityJobFixed       _correctVelocityJob;
        
        private WriteDensityFieldJobFixed  _writeDensityJob;
        private WriteVelocityFieldJobFixed _writeVelocityJob;

        public ref NativeArray<float> Exchange_Density => ref exchange_Density.back;

        public ref NativeArray<float> Exchange_VelocityX => ref exchange_VelocityX.back;
        public ref NativeArray<float> Exchange_VelocityY => ref exchange_VelocityY.back;
        public ref NativeArray<float> Exchange_VelocityZ => ref exchange_VelocityZ.back;

        private int3 dimensions;
        private int3 N;

        public int Length => dimensions.x * dimensions.y * dimensions.z;

        public int NSize => N.x * N.y * N.z;

        public int IteratorX => N.x;
        
        public bool IsCreated
        {
            get => exchange_Density.IsCreated   &&
                   exchange_VelocityX.IsCreated &&
                   exchange_VelocityY.IsCreated &&
                   exchange_VelocityZ.IsCreated;
        }

        public JobHandle _simlateJobHandle = default;

        public void Dispose()
        {
            if (!IsCreated)
                return;
            DensityField.Dispose();
            VelocityX.Dispose();
            VelocityY.Dispose();
            VelocityZ.Dispose();
            exchange_Density.Dispose();
            exchange_VelocityX.Dispose();
            exchange_VelocityY.Dispose();
            exchange_VelocityZ.Dispose();
        }

        public void Initialize(int3 dims, int3 n, int4 stride, Allocator allocator, bool overSampling = false)
        {
            dimensions = dims;
            this.N     = n;

            int length = dims.x * dims.y * dims.z;

            DensityField = new DoubleBufferFixed(dims, allocator, BoundaryType.Density);
            VelocityX    = new DoubleBufferFixed(dims, allocator,BoundaryType.VelocityX);
            VelocityY    = new DoubleBufferFixed(dims, allocator,BoundaryType.VelocityY);
            VelocityZ    = new DoubleBufferFixed(dims, allocator,BoundaryType.VelocityZ);
            
            exchange_Density   = new ExchangeBufferFixed(length, allocator, overSampling);
            exchange_VelocityX = new ExchangeBufferFixed(length, allocator, overSampling);
            exchange_VelocityY = new ExchangeBufferFixed(length, allocator, overSampling);
            exchange_VelocityZ = new ExchangeBufferFixed(length, allocator, overSampling);

            _forwardAdvectVelocityJob = new ForwardAdvectVelocityJobFixed()
            {
                dim      = dims,
                N        = N,
                stride   = stride,
                maxRange = new VInt3((float3)dims - 1.5f) 
            };
            
            _reverseAdvectVelocityJob = new ReverseAdvectVelocityJobFixed()
            {
                dim      = dims,
                N        = N,
                stride   = stride,
                maxRange = new VInt3((float3)dims - 1.5f) 
            };

            _divergenceJob      = new DivergenceJobFixed() { dim      = dims, N = N, stride = stride };
            _pressureJob        = new PressureJobFixed() { dim        = dims, N = N, stride = stride };
            _correctVelocityJob = new CorrectVelocityJobFixed() { dim = dims, N = N, stride = stride };

            _writeDensityJob = new WriteDensityFieldJobFixed()
            {
                dim  = dims, 
                // N = N, 
                // maxRange = (float3)dims - 1f,
                super    = overSampling,
            };

            _writeVelocityJob = new WriteVelocityFieldJobFixed()
            {
                dim      = dims,
                // N        = N,
                // maxRange = (float3)dims - 1f,
                super    = overSampling,
            };
        }

        public JobHandle WriteToFrontBuffer(JobHandle                             dep,
                                            ref NativeList<PBDForceField>         forceFields,
                                            ref NativeList<PBDCustomColliderInfo> colliders,
                                            in  float3                            windFieldOri,
                                            in  PBDBounds                         windFieldBounds,
                                            bool                                  useDensityField,
                                            bool                                  superSample)
        {
            
            dep = JobHandle.CombineDependencies(WriteDensity(dep, ref forceFields, ref colliders, in windFieldOri, in windFieldBounds, useDensityField, superSample),
                                                WriteVelocity(dep, ref forceFields, ref colliders, in windFieldOri, in windFieldBounds, useDensityField, superSample));

            return dep;
        }

        private JobHandle WriteDensity(JobHandle                             dep,
                                       ref NativeList<PBDForceField>         forceFields,
                                       ref NativeList<PBDCustomColliderInfo> colliders,
                                       in  float3                            windFieldOri,
                                       in  PBDBounds                         windFieldBounds,
                                       bool                                  useDensityField,
                                       bool                                  superSample)
        {
            if (!useDensityField)
                return dep;
            
            if (superSample)
            {
                _writeDensityJob.UpdateParams(ref exchange_Density.super, in windFieldOri, in windFieldBounds, ref colliders);

                dep = DensityField.ClearData(dep, ref exchange_Density.super);
                
                dep = _writeDensityJob.ScheduleByRef(dep);
                
                dep = exchange_Density.DownSample(dep, dimensions);
            }
            else
            {
                _writeDensityJob.UpdateParams(ref exchange_Density.front, in windFieldOri, in windFieldBounds, ref colliders);

                dep = DensityField.ClearData(dep, ref exchange_Density.front);
                
                dep = _writeDensityJob.ScheduleByRef(dep);
            }

            return dep;
        }
        
        private JobHandle WriteVelocity(JobHandle                             dep,
                                        ref NativeList<PBDForceField>         forceFields,
                                        ref NativeList<PBDCustomColliderInfo> colliders,
                                        in  float3                            windFieldOri,
                                        in  PBDBounds                         windFieldBounds,
                                        bool                                  useDensityField,
                                        bool                                  superSample)
        {
            if (superSample)
            {
                _writeVelocityJob.UpdateParams(ref exchange_VelocityX.super, ref exchange_VelocityY.super, ref exchange_VelocityZ.super,
                                               in windFieldOri, in windFieldBounds, ref forceFields);

                dep = JobHandle.CombineDependencies(VelocityX.ClearData(dep, ref exchange_VelocityX.super),
                                                    VelocityY.ClearData(dep, ref exchange_VelocityY.super),
                                                    VelocityZ.ClearData(dep, ref exchange_VelocityZ.super));

                dep = _writeVelocityJob.ScheduleByRef(dep);

                dep = JobHandle.CombineDependencies(exchange_VelocityX.DownSample(dep, dimensions),
                                                    exchange_VelocityY.DownSample(dep, dimensions),
                                                    exchange_VelocityZ.DownSample(dep, dimensions));
            }
            else
            {
                _writeVelocityJob.UpdateParams(ref exchange_VelocityX.front, ref exchange_VelocityY.front, ref exchange_VelocityZ.front,
                                               in windFieldOri, in windFieldBounds, ref forceFields);

                dep = JobHandle.CombineDependencies(VelocityX.ClearData(dep, ref exchange_VelocityX.front),
                                                    VelocityY.ClearData(dep, ref exchange_VelocityY.front),
                                                    VelocityZ.ClearData(dep, ref exchange_VelocityZ.front));

                dep = _writeVelocityJob.ScheduleByRef(dep);
            }

            return dep;
        }
        
        [GenerateTestsForBurstCompatibility]
        public JobHandle Simulate(JobHandle    dep,
                                          VInt         deltaTime,               VInt              vicosity,
                                          VInt         diffusion,               VInt              forwardAdvectRatio,
                                          int          iterationsCountPreFrame, bool              useDensityField,
                                          ParallelUnit parallelUnit,            ConvolutionMethod method)
        {
            if (IsCreated)
            {
                dep = VelocityStep(dep, deltaTime, vicosity, forwardAdvectRatio, iterationsCountPreFrame, parallelUnit, method);
                if(useDensityField)
                    dep = DensityStep(dep, deltaTime, diffusion, forwardAdvectRatio,iterationsCountPreFrame, parallelUnit, method);
            }
            return dep;
        }
        
        public JobHandle SaveToBack(JobHandle dep, bool useDensityField)
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

        public JobHandle DensityStep(JobHandle    dep,
                                             VInt         deltaTime, VInt diffusion,
                                             VInt         forwardAdvectRatio,
                                             int          iterationsCountPreFrame,
                                             ParallelUnit parallelUnit, ConvolutionMethod method)
        {
            dep = DensityField.AddSource(dep, ref exchange_Density.front, deltaTime, parallelUnit);

            dep = DensityField.Diffuse(dep, diffusion, deltaTime, iterationsCountPreFrame,
                                             parallelUnit, method);
            
            dep = DensityField.Advect(dep, ref VelocityX.back, ref VelocityY.back, ref VelocityZ.back, forwardAdvectRatio , deltaTime, parallelUnit);

            
            return dep;
        }

        public JobHandle VelocityStep(JobHandle    dep,
                                              VInt         deltaTime, VInt vicosity,
                                              VInt         forwardAdvectRatio,
                                              int          iterationsCountPreFrame,
                                              ParallelUnit parallelUnit, ConvolutionMethod method)
        {
            var addSourceX = VelocityX.AddSource(dep, ref exchange_VelocityX.front, deltaTime, parallelUnit);
            var addSourceY = VelocityY.AddSource(dep, ref exchange_VelocityY.front, deltaTime, parallelUnit);
            var addSourceZ = VelocityZ.AddSource(dep, ref exchange_VelocityZ.front, deltaTime, parallelUnit);

            var diffuseXJobHandle = VelocityX.Diffuse(addSourceX, vicosity, deltaTime, iterationsCountPreFrame,
                                                            parallelUnit, method);
            var diffuseYJobHandle = VelocityY.Diffuse(addSourceY, vicosity, deltaTime, iterationsCountPreFrame,
                                                            parallelUnit, method);
            var diffuseZJobHandle = VelocityZ.Diffuse(addSourceZ, vicosity, deltaTime, iterationsCountPreFrame,
                                                            parallelUnit, method);

            // dep = JobHandle.CombineDependencies(addSourceX, addSourceY, addSourceZ);
            
            dep = JobHandle.CombineDependencies(diffuseXJobHandle, diffuseYJobHandle, diffuseZJobHandle);


            dep = VelocityProject(dep, iterationsCountPreFrame, parallelUnit);


            VelocityX.Swap();
            VelocityY.Swap();
            VelocityZ.Swap();

            // clear
            dep = JobHandle.CombineDependencies(VelocityX.ClearBack(dep), VelocityY.ClearBack(dep), VelocityZ.ClearBack(dep));

            _forwardAdvectVelocityJob.UpdateParams(ref VelocityX.back, ref VelocityY.back, ref VelocityZ.back,
                                                   ref VelocityX.front, ref VelocityY.front, ref VelocityZ.front, deltaTime, forwardAdvectRatio);

            _reverseAdvectVelocityJob.UpdateParams(ref VelocityX.back, ref VelocityY.back, ref VelocityZ.back,
                                                   ref VelocityX.front, ref VelocityY.front, ref VelocityZ.front, deltaTime, FixedPointUtils.ScaleInv - forwardAdvectRatio);

            JobHandle reverseAdvect = default,
                      forwardAdvect = default;
            
            if (parallelUnit == ParallelUnit.Line)
            {
                if (forwardAdvectRatio < new VInt(0.9f))
                    reverseAdvect = _reverseAdvectVelocityJob.ScheduleByRef(NSize, IteratorX, dep);
                
                if (forwardAdvectRatio > new VInt(0.1f))
                    forwardAdvect = _forwardAdvectVelocityJob.ScheduleByRef(NSize, IteratorX, dep);
            }
            else
            {
                if (forwardAdvectRatio < new VInt(0.9f))
                    reverseAdvect = _reverseAdvectVelocityJob.ScheduleByRef(dep);
                
                if (forwardAdvectRatio > new VInt(0.1f))
                    forwardAdvect = _forwardAdvectVelocityJob.ScheduleByRef(dep);
            }

            dep = JobHandle.CombineDependencies(reverseAdvect, forwardAdvect);
            dep = JobHandle.CombineDependencies(VelocityX.SetBoundaryClampBack(dep),
                                                VelocityY.SetBoundaryClampBack(dep),
                                                VelocityZ.SetBoundaryClampBack(dep));

            dep = VelocityProject(dep, iterationsCountPreFrame, parallelUnit);

            return dep;
        }

        public JobHandle VelocityProject(JobHandle dep, int iterateCount, ParallelUnit parallelUnit)
        {
            ref NativeArray<VInt> pressure   = ref VelocityX.front, //
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
                
                // pressure
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

    }
}