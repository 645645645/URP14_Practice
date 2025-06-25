using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace UnityEngine.PBD
{
    public class ExchangeBufferFixed
    {
        private NativeArray<float> dataA;
        private NativeArray<VInt>  dataB;
        
        private NativeArray<VInt> dataC;// overSampling
        
        public ref NativeArray<float> back =>  ref dataA; //外部输入到风场
        public ref NativeArray<VInt>  front =>  ref dataB; //外部输入到风场

        public ref NativeArray<VInt> super => ref dataC;
        
        private DownSampleJobFixed _downSampleJob;
        
        public bool IsCreated
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => dataA.IsCreated && dataB.IsCreated;
        }

        public ExchangeBufferFixed(int length, Allocator allocator,  bool overSampling = false)
        {
            dataA = new NativeArray<float>(length, allocator, NativeArrayOptions.UninitializedMemory);
            dataB = new NativeArray<VInt>(length, allocator, NativeArrayOptions.UninitializedMemory);
            
            if (overSampling)
            {
                dataC          = new NativeArray<VInt>(length * 8, allocator, NativeArrayOptions.UninitializedMemory);
                _downSampleJob = new DownSampleJobFixed();
            }
        }

        public void Dispose()
        {
            if (IsCreated)
            {
                dataA.Dispose();
                dataB.Dispose();
                
                if (dataC.IsCreated)
                    dataC.Dispose();
            }
        }
        
        public JobHandle DownSample(JobHandle dep, in int3 dim)
        {
            if(dataC.IsCreated)
            {
                _downSampleJob.UpdateParams(ref front, ref super, in dim);
                dep = _downSampleJob.ScheduleByRef(dep);
            }

            return dep;
        }
    }
}