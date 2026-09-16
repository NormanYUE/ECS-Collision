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
        [ReadOnly] public NativeArray<Aabb> BodyBounds;

        /// <summary>Morton 排序后的稠密 body 下标（排序位置 → body）。</summary>
        [ReadOnly] public NativeArray<int> SortedOrder;

        [NativeDisableParallelForRestriction] public NativeArray<BvhNode> Nodes;

        public int BodyCount;

        /// <summary>叶数组长度（补足到 2 的幂）。</summary>
        public int LeafCapacity;

        public void Execute(int leafIndex)
        {
            unsafe
            {
                BvhBuilder.BuildLeaves(
                    (BvhNode*)Nodes.GetUnsafePtr(),
                    (Aabb*)BodyBounds.GetUnsafeReadOnlyPtr(),
                    (int*)SortedOrder.GetUnsafeReadOnlyPtr(),
                    BodyCount,
                    LeafCapacity);
            }
        }
    }
}
