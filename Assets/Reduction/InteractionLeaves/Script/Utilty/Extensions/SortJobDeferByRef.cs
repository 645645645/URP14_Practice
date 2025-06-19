using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;

namespace Unity.Collections
{
    
    [GenerateTestsForBurstCompatibility(GenericTypeArguments = 
        new[] { typeof(int), typeof(NativeSortExtension.DefaultComparer<int>) })]
    public unsafe struct SortJobDeferByRef<T, U>
        where T : unmanaged
        where U : IComparer<T>
    {
        public          NativeList<T> Data;
        public readonly U             Comp;


        public SortJobDeferByRef(ref NativeList<T> data, in U u)
        {
            Data = data;
            Comp = u;
        }        
        
        struct HeapNode
        {
            public T   Value;
            public int SegmentIndex;
            public int ElementIndex;
        }

        
        [BurstCompile(
            OptimizeFor = OptimizeFor.Performance, 
            FloatMode = FloatMode.Fast,          CompileSynchronously = true, 
            FloatPrecision = FloatPrecision.Low, DisableSafetyChecks = true)]
        struct SegmentSortDeferred : IJobParallelFor
        {
            [NativeDisableUnsafePtrRestriction]
            internal NativeArray<T> Data;

            internal U Comp;

            [ReadOnly] internal int JobNum;

            public void Execute(int index)
            {
                int length       = Data.Length;
                int segmentCount = math.min(JobNum, length);
//                if (length < 2 || JobNum == 0 || index >= segmentCount)
//                    return;

                // 将多个或判断合并为一个与
                if (!(length >= 2 && JobNum > 0 && index < segmentCount))
                    return;

                int segmentWidth = (length + segmentCount - 1) / segmentCount;
                int startIndex    = segmentWidth * index,
                    segmentLength = math.min(segmentWidth, length - startIndex);

                NativeSortExtension.Sort((T*)Data.GetUnsafePtr() + startIndex, segmentLength, Comp);

            }
        }

        [BurstCompile(OptimizeFor = OptimizeFor.Performance, 
            FloatMode = FloatMode.Fast, CompileSynchronously = true, 
            FloatPrecision = FloatPrecision.Low, DisableSafetyChecks = true)]
        struct SegmentSortMergeDeferred : IJob
        {
            [NativeDisableUnsafePtrRestriction]
            internal NativeArray<T> Data;
            internal U Comp;

            [ReadOnly] internal int JobNum;

            public void Execute()
            {
                int length       = Data.Length;
                int segmentCount = math.min(JobNum, length);
//                if (length < 2 || JobNum == 0 || segmentCount <= 1) return;

                if (!(length >= 2 && JobNum > 0 && segmentCount > 1)) return;

                int segmentWidth = (length + segmentCount - 1) / segmentCount;

                var dataPtr   = (T*)Data.GetUnsafePtr();
                var resultPtr = (T*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<T>() * length, 16, Allocator.Temp);

                // 预存每个段的结束位置
                int* segmentEnds = stackalloc int[segmentCount];
                for (int i = 0; i < segmentCount; i++)
                    segmentEnds[i] = math.min((i + 1) * segmentWidth, length);

                HeapNode* minHeap  = stackalloc HeapNode[segmentCount];
                int       heapSize = 0;

                for (int i = 0; i < segmentCount; i++)
                {
                    int segmentStart = i * segmentWidth;
                    if (segmentStart < length)
                    {
                        minHeap[heapSize++] = new HeapNode
                        {
                            Value        = dataPtr[segmentStart],
                            SegmentIndex = i,
                            ElementIndex = 0
                        };
                    }
                }
                

                for (int i = heapSize / 2 - 1; i >= 0; i--)
                    HeapifyDown(ref minHeap, heapSize, i);

                int resultIndex = 0;
                while (heapSize > 0)
                {
                    // 取出堆顶最小元素
                    HeapNode minNode     = minHeap[0];
                    resultPtr[resultIndex++] = minNode.Value;

                    // 计算该元素在原始数组中的位置
                    int segmentIdx       = minNode.SegmentIndex;
                    int nextElementIndex = minNode.ElementIndex + 1;
                    int absoluteIndex    = math.mad(segmentIdx, segmentWidth, nextElementIndex);

                    // 如果该分段还有元素
                    if (absoluteIndex < segmentEnds[segmentIdx])
                    {
                        // 替换堆顶元素为分段中的下一个元素
                        minNode.ElementIndex = nextElementIndex;
                        minNode.Value        = dataPtr[absoluteIndex];
                        minHeap[0]           = minNode;

                        // 下滤调整堆
                        HeapifyDown(ref minHeap, heapSize, 0);
                    }
                    else
                    {
                        minHeap[0] = minHeap[--heapSize];
                        if (heapSize > 0) HeapifyDown(ref minHeap, heapSize, 0);
                    }
                }

                UnsafeUtility.MemCpy(dataPtr, resultPtr, UnsafeUtility.SizeOf<T>() * length);
                
                UnsafeUtility.Free(resultPtr, Allocator.Temp);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void HeapifyDown(ref HeapNode* heap, int length, int index)
            {
                while (true)
                {
                    int smallest = index;
                    int left     = 2 * index + 1;
                    int right    = 2 * index + 2;

                    if (left < length && Comp.Compare(heap[left].Value, heap[smallest].Value) < 0)
                        smallest = left;

                    if (right < length && Comp.Compare(heap[right].Value, heap[smallest].Value) < 0)
                        smallest = right;

                    if (smallest == index) break;

                    (heap[index], heap[smallest]) = (heap[smallest], heap[index]);

                    index = smallest;
                }
            }
            
        }
        
        /// <summary>
        /// 实现虽挫，没性能bug
        /// </summary>
        /// <param name="inputDeps"></param>
        /// <param name="threadNum"></param>
        /// <returns></returns>
        [GenerateTestsForBurstCompatibility]
        public JobHandle ScheduleDeferredByRef(JobHandle inputDeps = default, int threadNum = -1)
        {
            if (threadNum < 1)
//#if UNITY_2022_2_14F1_OR_NEWER
#if UNITY_2022_3_OR_NEWER
                threadNum = JobsUtility.ThreadIndexCount;
#else
                threadNum = JobsUtility.MaxJobThreadCount;
#endif
            // var deferData = Data.AsDeferredJobArray();
            
            var segmentSortJob            = new SegmentSortDeferred() { Data = Data.AsDeferredJobArray(), Comp = Comp, JobNum = threadNum };
            var segementCombine           = segmentSortJob.ScheduleByRef(threadNum, 1, inputDeps);
            var segmentSortMergeJob       = new SegmentSortMergeDeferred() { Data = Data.AsDeferredJobArray(), Comp = Comp, JobNum = threadNum };
            var segmentSortMergeJobHandle = segmentSortMergeJob.ScheduleByRef(segementCombine);
            return segmentSortMergeJobHandle;
        }
    }
}
