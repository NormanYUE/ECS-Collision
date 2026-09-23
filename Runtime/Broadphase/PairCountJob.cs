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
        [NativeDisableUnsafePtrRestriction] public long NodesPtr;

        [NativeDisableUnsafePtrRestriction] public long BodyBoundsPtr;

        [NativeDisableUnsafePtrRestriction] public long SortedOrderPtr;

        /// <summary>遍历栈，按线程切片。</summary>
        [NativeDisableUnsafePtrRestriction] public long TraversalStackPtr;

        [NativeDisableUnsafePtrRestriction] public long PairCountsPtr;

        /// <summary>稠密 body 标志位（含 Enabled / Active 位）；配合 <see cref="SkipInactive"/> 过滤未参与体。</summary>
        [NativeDisableUnsafePtrRestriction] public long BodyFlagsPtr;

        [NativeSetThreadIndex] public int ThreadIndex;

        public int Root;
        public int BodyCount;
        public int StackDepth;

        /// <summary>是否跳过未参与碰撞的体（判定用 <see cref="ParticipationBits"/>）。传 0 位即不过滤。</summary>
        public byte ParticipationBits;

        public unsafe void Execute(int leafIndex)
        {
            var Nodes = (BvhNode*)NodesPtr;
            var BodyBounds = (Aabb*)BodyBoundsPtr;
            var SortedOrder = (int*)SortedOrderPtr;
            var TraversalStack = (int*)TraversalStackPtr;
            var PairCounts = (int*)PairCountsPtr;
            var BodyFlags = (byte*)BodyFlagsPtr;
            if (leafIndex >= BodyCount) return;

            unsafe
            {
                int stackOffset = ThreadIndex * StackDepth;
                int* stack = (int*)TraversalStack + stackOffset;
                int body = SortedOrder[leafIndex];

                Aabb bounds = BodyBounds[body];

                // 自身 / 对手的参与位过滤都在 BvhBuilder 里（单一事实来源，CLI 可测）。
                var result = BvhBuilder.CountHierarchyOverlaps(
                    (BvhNode*)Nodes, Root, leafIndex, &bounds, stack, StackDepth,
                    SortedOrder, BodyFlags, ParticipationBits);

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
