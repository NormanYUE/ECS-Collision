using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace Ember.Collision
{
    /// <summary>
    /// 构造 BVH 叶层（并行）。叶下标为<b>排序位置</b>；
    /// 补位叶（下标 ≥ bodyCount）标记为 <c>MaxLeaf = -1</c> 的空叶。
    /// </summary>
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct BvhLeafJob : IJobParallelFor
    {
        [NativeDisableUnsafePtrRestriction] public long BodyBoundsPtr;

        /// <summary>Morton 排序后的稠密 body 下标（排序位置 → body）。</summary>
        [NativeDisableUnsafePtrRestriction] public long SortedOrderPtr;

        [NativeDisableUnsafePtrRestriction] public long NodesPtr;

        public int BodyCount;

        /// <summary>叶数组长度（补足到 2 的幂）。</summary>
        public int LeafCapacity;

        public unsafe void Execute(int leafIndex)
        {
            var BodyBounds = (Aabb*)BodyBoundsPtr;
            var SortedOrder = (int*)SortedOrderPtr;
            var Nodes = (BvhNode*)NodesPtr;
            // 只写自己那一格。绝不能在这里调 BvhBuilder.BuildLeaves——
            // 那是「写整张数组」的入口，在并行分发下会退化成 O(leafCapacity²)（见 1.0.15）。
            BvhBuilder.BuildLeafAt((BvhNode*)Nodes, (Aabb*)BodyBounds, (int*)SortedOrder, BodyCount, leafIndex);
        }
    }
}
