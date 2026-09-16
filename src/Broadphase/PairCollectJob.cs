using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace Ember.Collision
{
    /// <summary>
    /// 候选 pair 写入（并行，每叶一次）。
    ///
    /// 写入位置 = <c>PairOffsets[leafIndex]</c>（前缀和），每叶的区间互不重叠，
    /// 因此完全并行、无锁、无竞争，且输出顺序仅取决于 Morton 排序（确定）。
    ///
    /// 输出下标约定：<c>BodyA &lt; BodyB</c>，两条遍历路径都会归一化到同一顺序，
    /// 保证下游（窄相 / 事件 / 求解）可以只用一套约定。
    /// </summary>
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct PairCollectJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<BvhNode> Nodes;

        [ReadOnly] public NativeArray<Aabb> BodyBounds;

        [ReadOnly] public NativeArray<int> SortedOrder;

        [ReadOnly] public NativeArray<int> PairOffsets;

        public NativeArray<int> PairCounts;

        [NativeDisableParallelForRestriction] public NativeArray<int> TraversalStack;

        [NativeDisableParallelForRestriction] public NativeArray<CandidatePair> Pairs;

        [NativeSetThreadIndex] public int ThreadIndex;

        public int Root;
        public int BodyCount;
        public int StackDepth;

        public void Execute(int leafIndex)
        {
            if (leafIndex >= BodyCount) return;

            int expected = PairCounts[leafIndex];
            if (expected <= 0) return;

            int offset = PairOffsets[leafIndex];
            int limit = Pairs.Length - offset;
            if (limit <= 0)
            {
                PairCounts[leafIndex] = -1;
                return;
            }

            if (limit > expected) limit = expected;

            unsafe
            {
                int stackOffset = ThreadIndex * StackDepth;
                int* stack = (int*)TraversalStack.GetUnsafePtr() + stackOffset;

                int body = SortedOrder[leafIndex];
                Aabb bounds = BodyBounds[body];

                var result = BvhBuilder.CollectPairsInto(
                    (BvhNode*)Nodes.GetUnsafeReadOnlyPtr(), Root, leafIndex, &bounds,
                    stack, StackDepth,
                    (int*)SortedOrder.GetUnsafeReadOnlyPtr(),
                    (CandidatePair*)Pairs.GetUnsafePtr(),
                    offset, limit);

                if (result.Overflow)
                    PairCounts[leafIndex] = -1;
            }
        }
    }
}
