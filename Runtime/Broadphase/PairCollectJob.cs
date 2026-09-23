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
        [NativeDisableUnsafePtrRestriction] public long NodesPtr;

        [NativeDisableUnsafePtrRestriction] public long BodyBoundsPtr;

        [NativeDisableUnsafePtrRestriction] public long SortedOrderPtr;

        [NativeDisableUnsafePtrRestriction] public long PairOffsetsPtr;

        [NativeDisableUnsafePtrRestriction] public long PairCountsPtr;

        [NativeDisableUnsafePtrRestriction] public long TraversalStackPtr;

        [NativeDisableUnsafePtrRestriction] public long PairsPtr;

        /// <summary>稠密 body 标志位（含 Enabled / Active 位）；配合 <see cref="SkipInactive"/> 过滤未参与体。</summary>
        [NativeDisableUnsafePtrRestriction] public long BodyFlagsPtr;

        [NativeSetThreadIndex] public int ThreadIndex;

        public int Root;
        public int BodyCount;
        public int StackDepth;
        public int PairCapacity;

        /// <summary>参与位（Enabled | Active）。传 0 即不过滤未参与体。</summary>
        public byte ParticipationBits;

        public unsafe void Execute(int leafIndex)
        {
            var Nodes = (BvhNode*)NodesPtr;
            var BodyBounds = (Aabb*)BodyBoundsPtr;
            var SortedOrder = (int*)SortedOrderPtr;
            var PairOffsets = (int*)PairOffsetsPtr;
            var TraversalStack = (int*)TraversalStackPtr;
            var Pairs = (CandidatePair*)PairsPtr;
            var PairCounts = (int*)PairCountsPtr;
            var BodyFlags = (byte*)BodyFlagsPtr;
            if (leafIndex >= BodyCount) return;

            int expected = PairCounts[leafIndex];
            if (expected <= 0) return;

            int offset = PairOffsets[leafIndex];
            int limit = PairCapacity - offset;
            if (limit <= 0)
            {
                PairCounts[leafIndex] = -1;
                return;
            }

            if (limit > expected) limit = expected;

            unsafe
            {
                int stackOffset = ThreadIndex * StackDepth;
                int* stack = (int*)TraversalStack + stackOffset;

                int body = SortedOrder[leafIndex];
                Aabb bounds = BodyBounds[body];

                var result = BvhBuilder.CollectPairsInto(
                    (BvhNode*)Nodes, Root, leafIndex, &bounds,
                    stack, StackDepth,
                    (int*)SortedOrder,
                    (CandidatePair*)Pairs,
                    offset, limit,
                    BodyFlags, ParticipationBits);

                if (result.Overflow)
                    PairCounts[leafIndex] = -1;
            }
        }
    }
}
