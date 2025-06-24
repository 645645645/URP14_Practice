using System;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace UnityEngine.PBD
{

    public abstract class DoubleBufferBase<T> : IDisposable
        where T : unmanaged
    {
        protected NativeArray<T> dataA;

        protected NativeArray<T> dataB;

        protected bool m_AisBackBuffer;

        protected int3 dimensions;
        protected int3 N;

        protected BoundaryType m_boundaryType;

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
        
        public bool IsCreated
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => dataA.IsCreated && dataB.IsCreated;
        }
        
        protected DoubleBufferBase(int3 dims, Allocator allocator, BoundaryType type = BoundaryType.Density){}
        
        public void Swap()
        {
            m_AisBackBuffer = !m_AisBackBuffer;
        }
        
        public void Swap(ref NativeArray<T> A, ref NativeArray<T> B)
        {
            (A, B) = (B, A);
        }
        
        
        public ref NativeArray<T> back =>  ref m_AisBackBuffer ? ref dataA : ref dataB; 
        

        public ref NativeArray<T> front=>  ref m_AisBackBuffer ?  ref dataB : ref  dataA;

        public void Dispose()
        {
            if (dataA.IsCreated) dataA.Dispose();
            if (dataB.IsCreated) dataB.Dispose();
        }
        
        protected void ExchangeBufferWithFront(ref NativeArray<T> source)
        {
            Swap(ref front, ref source);
        }

        public virtual JobHandle AddSource(JobHandle    dep, ref NativeArray<T> source, T deltaTime,
                                           ParallelUnit parallelUnit = ParallelUnit.Line)
        {
            throw new NotImplementedException();
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
        public virtual JobHandle Diffuse(JobHandle         dep, T ratio, T deltaTime, int iterateCount = 10,
                                 ParallelUnit      parallelUnit = ParallelUnit.Line,
                                 ConvolutionMethod convolution  = ConvolutionMethod.LineSlidingWindow)
        {
            throw new NotImplementedException();
        }
        
        public virtual JobHandle SetBoundaryClamp(JobHandle dep, ref NativeArray<T> data, int b)
        {
            throw new NotImplementedException();
        }
        
        public virtual JobHandle SetBoundaryClampBack(JobHandle dep)
        {
            return SetBoundaryClamp(dep, ref back, (int)m_boundaryType);
        }

        public virtual JobHandle ClearBack(JobHandle dep)
        {
            return dep;
        }

        public virtual JobHandle ClearData(JobHandle dep, ref NativeArray<T> data)
        {
            return dep;
        }

        public virtual JobHandle Advect(JobHandle          dep,
                                        ref NativeArray<T> u, ref NativeArray<T> v, ref NativeArray<T> w,
                                        T                  forwardAdvectRatio,
                                        T                  deltaTime,
                                        ParallelUnit       parallelUnit = ParallelUnit.Line)
        {
            throw new NotImplementedException();
        }
        
        public virtual JobHandle Save(JobHandle dep, ref NativeArray<float> data)
        {
            throw new NotImplementedException();
        }
    }
}