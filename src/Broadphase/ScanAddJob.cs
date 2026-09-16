using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace Ember.Collision
{
    /// <summary>扫描步骤 3（并行，每块一次）：叠加块的全局基址。</summary>
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct ScanAddJob : IJobParallelFor
    {
        [NativeDisableParallelForRestriction] public NativeArray<int> Destination;

        [NativeDisableParallelForRestriction] public NativeArray<int> BlockTotals;

        public int Count;
        public int BlockSize;

        public void Execute(int block)
        {
            unsafe
            {
                BlockScan.AddBlockBase(
                    (int*)Destination.GetUnsafePtr(),
                    block,
                    BlockScan.BlockStart(block, BlockSize),
                    BlockScan.BlockEnd(block, BlockSize, Count),
                    (int*)BlockTotals.GetUnsafePtr());
            }
        }
    }
}
