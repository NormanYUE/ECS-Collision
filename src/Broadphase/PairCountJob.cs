using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace Ember.Collision
{
    /// <summary>
    /// 候选 pair 计数（并行，每叶一次）。
    ///
    /// 每个叶只向<b>排序位置更大</b>的叶推进（<c>MaxLeaf &gt; leafIndex</c> 剪枝），
    /// 因此每个无序 pair 恰好被计数一次——无需去重集合、无需排序后再比对，
    /// 且结果与遍历顺序无关（确定性）。
    /// </summary>
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct PairCountJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<BvhNode> Nodes;

        [ReadOnly] public NativeArray<Aabb> BodyBounds;

        [ReadOnly] public NativeArray<int> SortedOrder;

        /// <summary>遍历栈，按线程切片。</summary>
        [NativeDisableParallelForRestriction] public NativeArray<int> TraversalStack;

        [NativeDisableParallelForRestriction] public NativeArray<int> PairCounts;

        [NativeSetThreadIndex] public int ThreadIndex;

        public int Root;
        public int BodyCount;
        public int StackDepth;

        public void Execute(int leafIndex)
        {
            if (leafIndex >= BodyCount) return;

            unsafe
            {
                int stackOffset = ThreadIndex * StackDepth;
                int* stack = (int*)TraversalStack.GetUnsafePtr() + stackOffset;
                int body = SortedOrder[leafIndex];
                Aabb bounds = BodyBounds[body];

                var result = BvhBuilder.CountHierarchyOverlaps(
                    (BvhNode*)Nodes.GetUnsafeReadOnlyPtr(), Root, leafIndex, &bounds, stack, StackDepth);

                if (result.Overflow)
                {
                    // 负值由后续串行归一化 Job 转为 0 并汇总诊断；
                    // 每个 leaf 只写自己的槽位，避免共享诊断位的并发写入。
                    PairCounts[leafIndex] = -1;
                    return;
                }

                PairCounts[leafIndex] = result.Count;
            }
        }
    }
}
