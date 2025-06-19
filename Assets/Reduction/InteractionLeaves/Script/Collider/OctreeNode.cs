using Unity.Collections;

namespace UnityEngine.PBD
{
    // 八叉树节点结构
    [GenerateTestsForBurstCompatibility]
    public struct OctreeNode
    {
        public PBDBounds Bounds;

        public ushort FirstChild;     // 8个子节点的连续索引
        public ushort RigidbodyStart; // 存储的刚体索引起始位置

        public ushort RigidbodyCount; // 节点中刚体数量

        // public int Capacity;
        public ushort ChildRigiCountSum;
        public byte Depth;
        public bool IsLeaf;

        // public bool IsFull => RigidbodyCount >= Capacity;
        public bool IsEmpty => NodeIsEmpty && ChildIsEmpty;

        public bool NodeIsEmpty => RigidbodyCount <= 0;

        public bool ChildIsEmpty => ChildRigiCountSum <= 0;
    }
}