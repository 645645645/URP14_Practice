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
    public unsafe struct SortJobByRef<T, U>
        where T : unmanaged
        where U : IComparer<T>
    {
        public          NativeArray<T> Data;
        public readonly U              Comp;

        public SortJobByRef(ref NativeArray<T> data, in U u)
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
            FloatMode = FloatMode.Fast, CompileSynchronously = true, 
            FloatPrecision = FloatPrecision.Low, DisableSafetyChecks = true)]
        struct SegmentSort : IJobParallelFor
        {
            [NativeDisableUnsafePtrRestriction]
            internal NativeArray<T> Data;

            internal U Comp;

            [ReadOnly] public int SegmentWidth;

            public void Execute(int index)
            {
                var startIndex    = index * SegmentWidth;
                var segmentLength = math.min(Data.Length - startIndex, SegmentWidth);
                NativeSortExtension.Sort((T*)Data.GetUnsafePtr() + startIndex, segmentLength, Comp);
            }
        }

        [BurstCompile(OptimizeFor = OptimizeFor.Performance, 
            FloatMode = FloatMode.Fast, CompileSynchronously = true, 
            FloatPrecision = FloatPrecision.Low, DisableSafetyChecks = true)]
        struct SegmentSortMerge : IJob
        {
            [NativeDisableUnsafePtrRestriction] 
            internal NativeArray<T> Data;
            internal U Comp;
            
            [ReadOnly] public int SegmentWidth;

            public void Execute()
            {
                int Length       = Data.Length;
                var segmentCount = (Length + (SegmentWidth - 1)) / SegmentWidth;
//                if (segmentCount <= 1) return;

                var dataPtr   = (T*)Data.GetUnsafePtr();
                var resultPtr = (T*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<T>() * Length, 16, Allocator.Temp);

                // 预存每个段的结束位置
                int* segmentEnds = stackalloc int[segmentCount];
                for (int i = 0; i < segmentCount; i++)
                    segmentEnds[i] = math.min((i + 1) * SegmentWidth, Length);

                HeapNode* minHeap  = stackalloc HeapNode[segmentCount];
                int       heapSize = 0;

                for (int i = 0; i < segmentCount; i++)
                {
                    int segmentStart = i * SegmentWidth;
                    if (segmentStart < Length)
                    {
                        minHeap[heapSize++] = new HeapNode
                        {
                            Value        = dataPtr[segmentStart],
                            SegmentIndex = i,
                            ElementIndex = 0
                        };
                    }
                }

                // 堆化
                for (int i = heapSize / 2 - 1; i >= 0; i--)
                    HeapifyDown(ref minHeap, heapSize, i);

                int resultIndex = 0;
                while (heapSize > 0)
                {
                    // 取出堆顶最小元素
                    HeapNode minNode = minHeap[0];
                    resultPtr[resultIndex++] = minNode.Value;

                    // 计算该元素在原始数组中的位置
                    int segmentIdx       = minNode.SegmentIndex;
                    int nextElementIndex = minNode.ElementIndex + 1;
                    int absoluteIndex    = math.mad(segmentIdx, SegmentWidth, nextElementIndex);

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
                        // 移除堆顶元素
                        minHeap[0] = minHeap[--heapSize];
                        if (heapSize > 0) HeapifyDown(ref minHeap, heapSize, 0);
                    }
                }

                UnsafeUtility.MemCpy(dataPtr, resultPtr, UnsafeUtility.SizeOf<T>() * Length);
                
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

        [GenerateTestsForBurstCompatibility]
        public JobHandle ScheduleByRef(JobHandle inputDeps = default)
        {
            int Length = Data.Length;
            if (Length == 0)
                return inputDeps;
            
            var segmentCount              = (Length + 1023) / 1024;
            var workerCount               = math.max(1, JobsUtility.MaxJobThreadCount);
            var workerSegmentCount        = segmentCount / workerCount;
            var segmentSortJob            = new SegmentSort { Data = Data, Comp = Comp, SegmentWidth = 1024 };
            var segmentSortJobHandle      = segmentSortJob.ScheduleByRef(segmentCount, workerSegmentCount, inputDeps);
            var segmentSortMergeJob       = new SegmentSortMerge { Data = Data, Comp = Comp, SegmentWidth = 1024 };
            var segmentSortMergeJobHandle = segmentSortMergeJob.ScheduleByRef(segmentSortJobHandle);
            return segmentSortMergeJobHandle;
        }
    }


}