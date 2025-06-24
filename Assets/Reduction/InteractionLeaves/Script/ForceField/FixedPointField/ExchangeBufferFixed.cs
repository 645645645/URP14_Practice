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
        
        public ref NativeArray<float> back =>  ref dataA; //外部输入到风场
        public ref NativeArray<VInt>  front =>  ref dataB; //外部输入到风场

        
        public bool IsCreated
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => dataA.IsCreated && dataB.IsCreated;
        }

        public ExchangeBufferFixed(int length, Allocator allocator)
        {
            dataA = new NativeArray<float>(length, allocator, NativeArrayOptions.ClearMemory);
            dataB = new NativeArray<VInt>(length, allocator, NativeArrayOptions.ClearMemory);
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