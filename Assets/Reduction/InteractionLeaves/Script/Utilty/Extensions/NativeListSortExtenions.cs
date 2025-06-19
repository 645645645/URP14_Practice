using System;
using System.Collections.Generic;

namespace Unity.Collections
{
    public static class NativeListSortExtenions
    {
        [GenerateTestsForBurstCompatibility(GenericTypeArguments = new[] { typeof(int) })]
        public static SortJobDeferByRef<T, NativeSortExtension.DefaultComparer<T>> SortJobDeferByRef<T>(ref this NativeList<T> array)
            where T : unmanaged, IComparable<T>
        {
            return new SortJobDeferByRef<T, NativeSortExtension.DefaultComparer<T>>(ref array, new NativeSortExtension.DefaultComparer<T>());
        }
        
        [GenerateTestsForBurstCompatibility(GenericTypeArguments = new[] { typeof(int), typeof(NativeSortExtension.DefaultComparer<int>) })]
        public static SortJobDeferByRef<T, U> SortJobDeferByRef<T, U>(ref this NativeList<T> array, in U comparer)
            where T : unmanaged
            where U : IComparer<T>
        {
            return new SortJobDeferByRef<T, U>(ref array, in comparer);
        }
    }
}