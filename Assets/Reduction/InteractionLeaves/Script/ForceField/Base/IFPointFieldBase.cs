using System;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace UnityEngine.PBD
{
    internal interface IFPointFieldBase<T> : IDisposable where T : unmanaged
    {
        void Initialize(int3 dims, int3 n, int4 stride, Allocator allocator, bool overSampling = false);


        public JobHandle WriteToFrontBuffer(JobHandle                             dep,
                                            ref NativeList<PBDForceField>         forceFields,
                                            ref NativeList<PBDCustomColliderInfo> colliders,
                                            in  float3                            windFieldOri,
                                            in  PBDBounds                         windFieldBounds,
                                            bool                                  useDensityField,
                                            bool                                  superSample);

        JobHandle Simulate(JobHandle    dep,
                                   T            deltaTime,               T                 vicosity,
                                   T            diffusion,               T                 forwardAdvectRatio,
                                   int          iterationsCountPreFrame, bool              useDensityField,
                                   ParallelUnit parallelUnit,            ConvolutionMethod method);
        
        JobHandle SaveToBack(JobHandle dep, bool useDensityField);

        JobHandle DensityStep(JobHandle    dep,       T deltaTime,
                                      T            diffusion, T forwardAdvectRatio,
                                      int          iterationsCountPreFrame,
                                      ParallelUnit parallelUnit, ConvolutionMethod method);

        JobHandle VelocityStep(JobHandle    dep,      T deltaTime,
                                       T            vicosity, T forwardAdvectRatio,
                                       int          iterationsCountPreFrame,
                                       ParallelUnit parallelUnit, ConvolutionMethod method);

        JobHandle VelocityProject(JobHandle dep, int iterateCount, ParallelUnit parallelUnit);
    }
}