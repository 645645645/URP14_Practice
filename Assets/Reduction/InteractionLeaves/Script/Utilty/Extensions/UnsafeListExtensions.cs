using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using System;

public static class UnsafeListExtensions
{
    /// <summary>
    /// No AtomicSafetyHandle, use Attribute [NativeDisableContainerSafetyRestriction]
    /// </summary>
    /// <param name="list"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    public static unsafe NativeArray<T> AsDeferredArrayUnsafe<T>(this UnsafeList<T> list) where T : unmanaged
    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS   
        if (list.Ptr == null)
                throw new InvalidOperationException("Cannot create a deferred job array from a null UnsafeList");
#endif
        byte* buffer = (byte*)list.Ptr;
        
        // 增加1字节偏移，标记为延迟模式
        // 这是Unity Job系统的内部机制，用于识别需要特殊处理的数组
        buffer += 1;
        
        var array = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(buffer, 0, Allocator.Invalid);
        
#if ENABLE_UNITY_COLLECTIONS_CHECKS   
        
//        AtomicSafetyHandle safetyHandle = AtomicSafetyHandle.Create();
//        NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref array, safetyHandle);
//        
//        AtomicSafetyHandle.SetBumpSecondaryVersionOnScheduleWrite(safetyHandle, true);
//        AtomicSafetyHandle.SetStaticSafetyId(ref safetyHandle, AtomicSafetyHandle.NewStaticSafetyId<NativeArray<T>>());
        
#endif
        return array;
    }
}