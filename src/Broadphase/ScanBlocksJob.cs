using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace Ember.Collision
{
    /// <summary>
    /// 扫描步骤 2（串行，仅块数级迭代）：块总和前缀和，
    /// 并把全局总数写入 <c>BlockTotals[BlockCount]</c>——
    /// 该总数即下游（pair / contact）的精确容量需求。
    /// </summary>
    [BurstCompile]
    public struct ScanBlocksJob : IJob
    {
        [NativeDisableParallelForRestriction] public NativeArray<int> BlockTotals;

        public int BlockCount;

        public void Execute()
        {
            unsafe
            {
                BlockScan.PrefixBlockTotals((int*)BlockTotals.GetUnsafePtr(), BlockCount);
            }
        }
    }
}
