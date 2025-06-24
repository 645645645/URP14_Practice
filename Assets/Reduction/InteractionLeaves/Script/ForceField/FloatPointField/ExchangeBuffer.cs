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
        private NativeArray<float>  dataB;
        
        public ref NativeArray<float> back =>  ref dataA;//风场输出到外部 
        
        public ref NativeArray<float> front =>  ref dataB;//外部输入到风场
        
        public bool IsCreated
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => dataA.IsCreated && dataB.IsCreated;
        }

        public ExchangeBuffer(int length, Allocator allocator)
        {
            dataA = new NativeArray<float>(length, allocator, NativeArrayOptions.ClearMemory);
            dataB = new NativeArray<float>(length, allocator, NativeArrayOptions.ClearMemory);
        }

        public void Dispose()
        {
            if (IsCreated)
            {
                dataA.Dispose();
                dataB.Dispose();
            }
        }
    }
}