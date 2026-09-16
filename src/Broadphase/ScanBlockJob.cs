using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace Ember.Collision
{
    /// <summary>扫描步骤 1（并行，每块一次）：块内独占前缀和 + 块总和。</summary>
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct ScanBlockJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<int> Source;

        [NativeDisableParallelForRestriction] public NativeArray<int> Destination;

        [NativeDisableParallelForRestriction] public NativeArray<int> BlockTotals;

        public int Count;
        public int BlockSize;

        public void Execute(int block)
        {
            unsafe
            {
                BlockScan.ScanBlock(
                    (int*)Source.GetUnsafeReadOnlyPtr(),
                    (int*)Destination.GetUnsafePtr(),
                    block,
                    BlockScan.BlockStart(block, BlockSize),
                    BlockScan.BlockEnd(block, BlockSize, Count),
                    (int*)BlockTotals.GetUnsafePtr());
            }
        }
    }
}
