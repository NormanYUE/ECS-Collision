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
        [NativeDisableUnsafePtrRestriction] public long SourcePtr;

        [NativeDisableUnsafePtrRestriction] public long DestinationPtr;

        [NativeDisableUnsafePtrRestriction] public long BlockTotalsPtr;

        public int Count;
        public int BlockSize;

        public unsafe void Execute(int block)
        {
            var Source = (int*)SourcePtr;
            var Destination = (int*)DestinationPtr;
            var BlockTotals = (int*)BlockTotalsPtr;
            unsafe
            {
                BlockScan.ScanBlock(
                    (int*)Source,
                    (int*)Destination,
                    block,
                    BlockScan.BlockStart(block, BlockSize),
                    BlockScan.BlockEnd(block, BlockSize, Count),
                    (int*)BlockTotals);
            }
        }
    }
}
