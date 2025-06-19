using System;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Collections
{
    public static class UnsafeListSortExtensions
    {
        [GenerateTestsForBurstCompatibility(GenericTypeArguments = new[] { typeof(int) })]
        public static SortJobDeferByRefUnsafe<T, NativeSortExtension.DefaultComparer<T>> SortJobDeferByRefUnsafe<T>(ref this UnsafeList<T> array)
            where T : unmanaged, IComparable<T>
        {
            return new SortJobDeferByRefUnsafe<T, NativeSortExtension.DefaultComparer<T>>(ref array, new NativeSortExtension.DefaultComparer<T>());
        }

        [GenerateTestsForBurstCompatibility(GenericTypeArguments = new[] { typeof(int), typeof(NativeSortExtension.DefaultComparer<int>) })]
        public static SortJobDeferByRefUnsafe<T, U> SortJobDeferByRefUnsafe<T, U>(ref this UnsafeList<T> array, in U comparer)
            where T : unmanaged
            where U : IComparer<T>
        {
            return new SortJobDeferByRefUnsafe<T, U>(ref array, in comparer);
        }
    }
}