using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace UnityEngine.PBD
{
    //交换指针调试困难 老翻车 已老实，最后稳定再换
    [GenerateTestsForBurstCompatibility]
    public class WindFiledExchangeBuffer
    {
        private NativeArray<float> dataA;
        private NativeArray<float> dataB;
        
        public ref NativeArray<float> back =>  ref dataA; 
        
        public ref NativeArray<float> front =>  ref dataB;

        // private    NativeArray<float> bedug;
        // public ref NativeArray<float> Debug => ref bedug;
        
        public bool IsCreated
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => dataA.IsCreated && dataB.IsCreated;
        }

        public WindFiledExchangeBuffer(int length, Allocator allocator)
        {
            dataA = new NativeArray<float>(length, allocator, NativeArrayOptions.ClearMemory);
            dataB = new NativeArray<float>(length, allocator, NativeArrayOptions.ClearMemory);
            
            // bedug = new NativeArray<float>(length, allocator, NativeArrayOptions.UninitializedMemory);
        }

        public void Dispose()
        {
            if (IsCreated)
            {
                dataA.Dispose();
                dataB.Dispose();

                // bedug.Dispose();
            }
        }
    }
}