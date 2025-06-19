using System;
using System.Collections.Generic;

namespace Unity.Collections
{
    public static class NativeArraySortExtenions
    {
        [GenerateTestsForBurstCompatibility(GenericTypeArguments = new[] { typeof(int) })]
        public static SortJobByRef<T, NativeSortExtension.DefaultComparer<T>> SortJobByRef<T>(ref this NativeArray<T> array)
            where T : unmanaged, IComparable<T>
        {
            return new SortJobByRef<T, NativeSortExtension.DefaultComparer<T>>(ref array, new NativeSortExtension.DefaultComparer<T>());
        }


        [GenerateTestsForBurstCompatibility(GenericTypeArguments = new[] { typeof(int), typeof(NativeSortExtension.DefaultComparer<int>) })]
        public static SortJobByRef<T, U> SortJobByRef<T, U>(ref this NativeArray<T> array, in U comparer)
            where T : unmanaged
            where U : IComparer<T>
        {
            return new SortJobByRef<T, U>(ref array, in comparer);
        }
    }
    
}