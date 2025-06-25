using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace UnityEngine.PBD
{
    [GenerateTestsForBurstCompatibility]
    public class ExchangeBuffer
    {
        private NativeArray<float> dataA;
        private NativeArray<float> dataB;

        private NativeArray<float> dataC;// overSampling
        
        public ref NativeArray<float> back =>  ref dataA;//风场输出到外部 
        
        public ref NativeArray<float> front => ref dataB;//外部输入到风场

        public ref NativeArray<float> super => ref dataC;

        private DownSampleJob _downSampleJob;
        
        public bool IsCreated
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => dataA.IsCreated && dataB.IsCreated;
        }

        public ExchangeBuffer(int length, Allocator allocator,  bool overSampling = false)
        {
            dataA = new NativeArray<float>(length, allocator, NativeArrayOptions.UninitializedMemory);
            dataB = new NativeArray<float>(length, allocator, NativeArrayOptions.UninitializedMemory);
            
            if (overSampling)
            {
                dataC          = new NativeArray<float>(length * 8, allocator, NativeArrayOptions.UninitializedMemory);
                _downSampleJob = new DownSampleJob();
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