using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace UnityEngine.PBD
{
    // 八叉树容器
    [GenerateTestsForBurstCompatibility]
    public struct RigidbodyOctree
    {
        public NativeList<OctreeNode> Nodes;
        public NativeList<ushort> RigidbodyIndices; // 所有节点存储的刚体索引连续数组
        public int NodeLength => Nodes.Length;
        public bool IsCreated => Nodes.IsCreated && RigidbodyIndices.IsCreated;

        public RigidbodyOctree(int capacity)
        {
            Nodes            = new NativeList<OctreeNode>(capacity, Allocator.Persistent);
            RigidbodyIndices = new NativeList<ushort>(capacity, Allocator.Persistent);
        }

        public void Clear()
        {
            if (!IsCreated)
                return;

            Nodes.Clear();
            RigidbodyIndices.Clear();
        }

        public void Dispose()
        {
            if (Nodes.IsCreated) Nodes.Dispose();
            if (RigidbodyIndices.IsCreated) RigidbodyIndices.Dispose();
        }

        [BurstCompile(OptimizeFor = OptimizeFor.Performance, FloatMode = FloatMode.Fast, 
                      FloatPrecision = FloatPrecision.Low, CompileSynchronously = true,
                      /*Debug = true,*/
                      DisableSafetyChecks = true)]
        internal unsafe struct BuildRigidBodyOctreeJob : IJob
        {
            [ReadOnly, NativeDisableParallelForRestriction]
            public NativeArray<PBDCustomColliderInfo>.ReadOnly Colliders;

            [ReadOnly, NativeDisableParallelForRestriction]
            public NativeArray<PBDBounds>.ReadOnly SceneBounds;

            [NativeDisableUnsafePtrRestriction] public RigidbodyOctree Octree;

            [ReadOnly] public int OctreeSplitThresholdNum;
            [ReadOnly] public int MaxDepth;

            public void Execute()
            {
                PBDBounds rootBound = SceneBounds[0];
                float     maxSize   = math.cmax(rootBound.Size);
                rootBound = new PBDBounds()
                {
                    Min = rootBound.Min,
                    Max = rootBound.Min + maxSize
                };

                OctreeNode root = new OctreeNode
                {
                    Bounds         = rootBound,
                    IsLeaf         = true,
                    RigidbodyStart = 0,
                    RigidbodyCount = (ushort)Colliders.Length,
                    Depth          = 0,
                };
                Octree.Nodes.Add(root);

                ushort i = 0;

                NativeArray<PBDBounds> childBounds  = new NativeArray<PBDBounds>(8, Allocator.Temp, 
                                                                                 NativeArrayOptions.UninitializedMemory);
                NativeList<ushort>     validIndices = new NativeList<ushort>(Octree.RigidbodyIndices.Capacity, Allocator.Temp);
                NativeList<ushort>     colliders    = new NativeList<ushort>(Colliders.Length,                 Allocator.Temp);
                NativeQueue<ushort>    queue        = new NativeQueue<ushort>(Allocator.Temp);
                for (; i < Colliders.Length; i++)
                    validIndices.AddNoResize(i);

                queue.Enqueue(0);

                while (queue.TryDequeue(out var nodeIndex))
                {
                    ref var node = ref Octree.Nodes.ElementAt(nodeIndex);

                    ushort firstChildIndex = (ushort)Octree.Nodes.Length;
                    node.FirstChild = firstChildIndex;
                    node.IsLeaf     = false;

                    if (node.RigidbodyCount > OctreeSplitThresholdNum)
                    {
                        if (node.Depth >= MaxDepth)
                            continue;

                        colliders.AddRangeNoResize(
                            (ushort*)validIndices.GetUnsafeReadOnlyPtr() + node.RigidbodyStart,
                            node.RigidbodyCount);

                        SplitBounds(in node.Bounds, childBounds);

                        ushort baseIndex    = (ushort)validIndices.Length;
                        byte   currentDepth = (byte)(node.Depth + 0x1);

                        for (i = 0; i < 8; i++)
                        {
                            OctreeNode child = new OctreeNode()
                            {
                                Bounds         = childBounds[i],
                                IsLeaf         = true,
                                RigidbodyCount = 0,
                                Depth          = currentDepth,
                            };
                            Octree.Nodes.Add(child);
                        }

                        for (i = 0; i < 8; i++)
                        {
                            var childIndex = firstChildIndex + i;

                            ushort rigiCount = 0;


                            for (int j = colliders.Length - 1; j >= 0; j--)
                            {
                                ref var   rbIndex  = ref colliders.ElementAt(j);
                                PBDBounds rbBounds = Colliders[rbIndex].Bounds;

                                if (MathematicsUtil.AABBContains(childBounds[i], in rbBounds))
                                {
                                    validIndices.Add(rbIndex);
                                    colliders.RemoveAtSwapBack(j);
                                    rigiCount++;
                                }
                            }

                            ref var childNode = ref Octree.Nodes.ElementAt(childIndex);
                            childNode.RigidbodyStart = baseIndex;
                            childNode.RigidbodyCount = rigiCount;

                            baseIndex += rigiCount;
                        }

                        i = (ushort)colliders.Length;
                        //不能完全放进子节点就留在父节点
                        if (i > 0)
                            UnsafeUtility.MemCpy(
                                destination: (ushort*)validIndices.GetUnsafePtr() + node.RigidbodyStart,
                                source: colliders.GetUnsafeReadOnlyPtr(),
                                size: sizeof(ushort) * i);

                        node.ChildRigiCountSum = (ushort)(node.RigidbodyCount - i);
                        node.RigidbodyCount    = i;

                        colliders.Clear();

                        for (i = 0; i < 8; i++)
                            if (Octree.Nodes.ElementAt(firstChildIndex + i).RigidbodyCount > OctreeSplitThresholdNum)
                                queue.Enqueue((ushort)(firstChildIndex + i));
                    }
                }

                //去空
                if (Octree.NodeLength < 1 || validIndices.IsEmpty)
                    return;

                ushort currentStart = 0;
                for (i = 0; i < Octree.Nodes.Length; i++)
                {
                    ref var node     = ref Octree.Nodes.ElementAt(i);
                    var     oldStart = node.RigidbodyStart;
                    var     count    = node.RigidbodyCount;

                    node.RigidbodyStart = currentStart;

                    if (count <= 0)
                        continue;

                    Octree.RigidbodyIndices.AddRangeNoResize(
                        (ushort*)validIndices.GetUnsafePtr() + oldStart,
                        count);

                    currentStart += count;
                }

                childBounds.Dispose();
                validIndices.Dispose();
                colliders.Dispose();
                queue.Dispose();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void SplitBounds(in PBDBounds bounds, NativeArray<PBDBounds> childBounds)
        {
            float3 center = (bounds.Min + bounds.Max) * 0.5f;
            int    index  = 0;

            for (int z = 0; z < 2; z++)
            {
                for (int y = 0; y < 2; y++)
                {
                    for (int x = 0; x < 2; x++)
                    {
                        bool3 selecter = new bool3(x == 0, y == 0, z == 0);
                        childBounds[index++] = new PBDBounds
                        {
                            Min = math.select(center,     bounds.Min, selecter),
                            Max = math.select(bounds.Max, center,     selecter)
                        };
                    }
                }
            }
        }
        
    }

    
    
}