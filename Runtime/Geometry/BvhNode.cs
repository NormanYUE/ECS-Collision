using Unity.Mathematics;

namespace Ember.Collision
{
    /// <summary>
    /// 宽相 BVH 节点。采用「叶节点在前、内部节点在后」的扁平布局：
    /// 叶节点区间为 [0, leafCount)，内部节点自 leafCount 起。
    ///
    /// <see cref="MinLeaf"/> / <see cref="MaxLeaf"/> 记录子树覆盖的叶下标区间，
    /// 使自查询可以 O(1) 剪掉「不含更大下标叶子」的子树——
    /// 这是「每个 pair 只产出一次、无需去重集合」的关键。
    /// </summary>
    public struct BvhNode
    {
        /// <summary>子树包围盒。</summary>
        public Aabb Bounds;

        /// <summary>内部节点：左子节点下标。叶节点：自身叶下标。</summary>
        public int Left;

        /// <summary>内部节点：右子节点下标。叶节点：-1。</summary>
        public int Right;

        /// <summary>子树中最小叶下标（含）。</summary>
        public int MinLeaf;

        /// <summary>子树中最大叶下标（含）。</summary>
        public int MaxLeaf;

        /// <summary>父节点下标；根为 -1。</summary>
        public int Parent;

        /// <summary>是否叶节点。</summary>
        public readonly bool IsLeaf => Right < 0;

        /// <summary>构造叶节点。</summary>
        public static BvhNode Leaf(in Aabb bounds, int leafIndex, int parent, int depth)
        {
            return new BvhNode
            {
                Bounds = bounds,
                Left = leafIndex,
                Right = -1,
                MinLeaf = leafIndex,
                MaxLeaf = leafIndex,
                Parent = parent,
                Depth = depth,
            };
        }

        /// <summary>构造内部节点。</summary>
        public static BvhNode Internal(
            in Aabb bounds, int left, int right, int minLeaf, int maxLeaf, int parent, int depth)
        {
            return new BvhNode
            {
                Bounds = bounds,
                Left = left,
                Right = right,
                MinLeaf = minLeaf,
                MaxLeaf = maxLeaf,
                Parent = parent,
                Depth = depth,
            };
        }

        /// <summary>节点深度。</summary>
        public int Depth;
    }
}
