using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace UnityEngine.PBD
{
    [GenerateTestsForBurstCompatibility]
    public unsafe struct SimpleHashArray<T> 
        where T : unmanaged
    {
        [NativeDisableUnsafePtrRestriction]
        private T* values;
        
        [NativeDisableContainerSafetyRestriction]
        private int* keys;
        
        private int count;
        private readonly int capacity;
            
        public readonly int  Count    => count;
        public readonly int  Capacity => capacity;
        public readonly bool IsEmpty  => !IsCreated || count == 0;

        private readonly Allocator Allocator;

        private readonly int BucketMask;
        private const    int emptyValue = 0x00;//
        
        public readonly bool IsCreated
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => values != null;
        }
        
        public SimpleHashArray(int minCapacity, Allocator allocator)
        {
            capacity = MathematicsUtil.NextPowerOfTwo(minCapacity);
            int keyLength = capacity >> 5;
            values    = (T*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<T>()     * capacity,  32, allocator);
            keys      = (int*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<int>() * keyLength, 32, allocator);
            Allocator = allocator;

            UnsafeUtility.MemSet(keys, emptyValue, UnsafeUtility.SizeOf<int>() * keyLength);

            count = 0;
            BucketMask = capacity - 1;
        }

        public bool TryAdd(int key, T Value)
        {
            int idx = key & BucketMask;

            int  bitIdx  = idx >> 5;
            
            int bitMask = 1 << (idx & 31);
            
            if ((keys[bitIdx] & bitMask) != emptyValue)
                return false;

            keys[bitIdx] |= bitMask;
            values[idx]  =  Value;
            count++;
            return true;
        }

        public T this[int key]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                TryGetValue(key, out T result);
                return result;
            }
            set => TryAdd(key, value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetValue(int key, out T value)
        {
            int idx = key & BucketMask;
            
            int  bitIdx  = idx >> 5;
            
            int bitMask = 1 << (idx & 31);

            if ((keys[bitIdx] & bitMask) != emptyValue)
            {
                value = values[idx];
                return true;
            }

            value = default;
            return false;
        }

        public bool TryRemove(int key)
        {
            int idx = key & BucketMask;
            
            int  bitIdx  = idx >> 5;
            
            int bitMask = 1 << (idx & 31);

            if ((keys[bitIdx] & bitMask) != emptyValue)
            {
                keys[bitIdx] ^= bitMask;
                return true;
            }

            return false;
        }

        public void Clear()
        {
            if (IsCreated)
            {
                int keyLength = capacity >> 5;

                UnsafeUtility.MemSet(keys, emptyValue, UnsafeUtility.SizeOf<int>() * keyLength);

                count = 0;
            }
        }

        public void Dispose()
        {
            if (!IsCreated)
                return;
            UnsafeUtility.Free(keys,   Allocator);
            UnsafeUtility.Free(values, Allocator);
            
            values = null;
            keys   = null;
            count  = 0;
        }
    }
}