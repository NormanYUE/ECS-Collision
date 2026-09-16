using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace Ember.Collision
{
    /// <summary>
    /// 分块包围盒归约第一步（并行）：每个分块求出本块 AABB 并集。
    /// 用分块归约而非单线程线性扫描，是为了让 1M 级实体数下
    /// 「全局包围盒」这一步不成为串行瓶颈。
    /// </summary>
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct BoundsReduceJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Aabb> BodyBounds;

        /// <summary>输出：每块 AABB（下标 0..blockCount-1）。</summary>
        [NativeDisableParallelForRestriction] public NativeArray<Aabb> BlockBounds;

        public int BodyCount;
        public int BlockSize;

        public void Execute(int block)
        {
            int start = block * BlockSize;
            int end = start + BlockSize;
            if (end > BodyCount) end = BodyCount;

            Aabb accumulated = Aabb.Empty;
            for (int i = start; i < end; i++)
                accumulated.Encapsulate(BodyBounds[i]);

            BlockBounds[block] = accumulated;
        }
    }
}
