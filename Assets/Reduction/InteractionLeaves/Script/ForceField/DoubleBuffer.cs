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
    public unsafe class DoubleBuffer : IDisposable
    {
        [NativeDisableUnsafePtrRestriction] private NativeArray<float> dataA;

        [NativeDisableUnsafePtrRestriction] private NativeArray<float> dataB;

        private bool m_AisBackBuffer;

        private readonly int3 dimensions;
        private readonly int3 dimensionsMask;
        private readonly int3 N;

        private readonly BoundaryType m_boundaryType;
        public int Length
        {
            get
            {
                return dimensions.x * dimensions.y * dimensions.z;
            }
        }


        public int BatchCountX => dimensions.x;
        public int BatchCountZ => dimensions.z;
        public int BatchCountY => dimensions.y;

        public int  NSize => N.x * N.y * N.z;
        
        public int IteratorX => N.x;
        public int IteratorZ => N.z;
        public int IteratorY => N.y;
        
        // public int GetIteratorDir

        private AddSourceJob _addSourceJob;
        private DiffuseXJob  _diffuseXJob;
        private DiffuseYJob  _diffuseYJob;
        private DiffuseZJob  _diffuseZJob;
        private AdvectJob    _advectJob;

        private SetBoundaryParallelForJob _setBoundaryParallelForJob;

        private WindSimulateSaveJob _saveJob;
        
        public bool IsCreated
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            // get => dataA != null && dataB != null;
            get => dataA.IsCreated && dataB.IsCreated;
        }

        public DoubleBuffer(int3 dims, Allocator allocator, BoundaryType type = BoundaryType.Density)
        {
            dimensions      = dims;
            dimensionsMask  = dims - 1;
            m_AisBackBuffer = true;
            m_boundaryType  = type;
            N               = dims - 2;

            int length = dimensions.x * dimensions.y * dimensions.z;

            int size = UnsafeUtility.SizeOf<float>() * length;

            dataA = new NativeArray<float>(length, allocator);
            dataB = new NativeArray<float>(length, allocator);

            _addSourceJob = new AddSourceJob() { };
            _diffuseXJob  = new DiffuseXJob() { dim = dimensions, N = N, };
            _diffuseYJob  = new DiffuseYJob() { dim = dimensions, N = N };
            _diffuseZJob  = new DiffuseZJob() { dim = dimensions, N = N };
            _advectJob    = new AdvectJob() { dim   = dimensions, N = N, };

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

        public JobHandle AddSource(JobHandle dep, ref NativeArray<float> source, float deltaTime)
        {
            ExchangeBufferWithFront(ref source);
            
            _addSourceJob.UpdateParams(ref back, ref front, deltaTime);

            return _addSourceJob.ScheduleByRef(Length, BatchCountX, dep);
        }

        /// <summary>
        /// 密度场为Diffusion，速度场为viscosty
        /// </summary>
        /// <param name="dep"></param>
        /// <param name="ratio"></param>
        /// <param name="deltaTime"></param>
        /// <param name="iterateCount"></param>
        /// <param name="convolution"></param>
        /// <returns></returns>
        public JobHandle Diffuse(JobHandle dep, float ratio, float deltaTime, int iterateCount = 10, 
                                 ConvolutionMethod convolution = ConvolutionMethod.LineSequence)
        {
            Swap();
            
            float  a                = deltaTime * ratio * (N.x * N.y * N.z);

            float  div              = 1 / math.mad(2, a, 1);
            float  absorption       = a * div;
            float2 DivAndAbsorption = new float2(div, absorption);

            _diffuseXJob.UpdateParams(ref back, ref front, ratio, deltaTime, DivAndAbsorption, convolution);
            _diffuseYJob.UpdateParams(ref back, ref front, ratio, deltaTime, DivAndAbsorption, convolution);
            _diffuseZJob.UpdateParams(ref back, ref front, ratio, deltaTime, DivAndAbsorption, convolution);
            
            for (int i = 0; i < iterateCount; i++)
            {
                dep = _diffuseXJob.ScheduleByRef(NSize, IteratorX, dep);
                dep = _diffuseYJob.ScheduleByRef(NSize, IteratorY, dep);
                dep = _diffuseZJob.ScheduleByRef(NSize, IteratorZ, dep);
                    
                dep = SetBoundaryClampBack(dep);
            }
            // switch (m_boundaryType)
            // {
            //     case BoundaryType.Density:
            //         
            //         for (int i = 0; i < iterateCount; i++)
            //         {
            //             dep = _diffuseXJob.ScheduleByRef(NSize, IteratorX, dep);
            //             dep = _diffuseYJob.ScheduleByRef(NSize, IteratorY, dep);
            //             dep = _diffuseZJob.ScheduleByRef(NSize, IteratorZ, dep);
            //         
            //             dep = SetBoundaryClampBack(dep);
            //         }
            //         break;
            //     case BoundaryType.VelocityX:
            //         for (int i = 0; i < iterateCount; i++)
            //         {
            //             dep = _diffuseXJob.ScheduleByRef(NSize, IteratorX, dep);
            //         
            //             dep = SetBoundaryClampBack(dep);
            //         }
            //         break;
            //     case BoundaryType.VelocityY:
            //     
            //         for (int i = 0; i < iterateCount; i++)
            //         {
            //             dep = _diffuseYJob.ScheduleByRef(NSize, IteratorY, dep);
            //         
            //             dep = SetBoundaryClampBack(dep);
            //         }
            //         break;
            //     case BoundaryType.VelocityZ:
            //     
            //         for (int i = 0; i < iterateCount; i++)
            //         {
            //             dep = _diffuseZJob.ScheduleByRef(NSize, IteratorZ, dep);
            //         
            //             dep = SetBoundaryClampBack(dep);
            //         }
            //         break;
            //     
            // }

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

        //density
        public JobHandle Advect(JobHandle dep, ref NativeArray<float> u, ref NativeArray<float> v, ref NativeArray<float> w, float deltaTime)
        {
            // Swap();

            _advectJob.UpdateParams(ref back, ref front, ref u, ref v, ref w, deltaTime);

            dep = _advectJob.ScheduleByRef(NSize, IteratorX, dep);

            dep = SetBoundaryClampBack(dep);
            return dep;
        }


        public JobHandle Save(JobHandle dep, ref NativeArray<float> data)
        {
            _saveJob.UpdateParams(ref back, ref data);
            
            return _saveJob.ScheduleByRef(dep);
        }

    }
}