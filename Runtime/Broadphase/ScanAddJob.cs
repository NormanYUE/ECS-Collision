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
        [NativeDisableUnsafePtrRestriction] public long DestinationPtr;

        [NativeDisableUnsafePtrRestriction] public long BlockTotalsPtr;

        public int Count;
        public int BlockSize;

        public unsafe void Execute(int block)
        {
            var Destination = (int*)DestinationPtr;
            var BlockTotals = (int*)BlockTotalsPtr;
            unsafe
            {
                BlockScan.AddBlockBase(
                    (int*)Destination,
                    block,
                    BlockScan.BlockStart(block, BlockSize),
                    BlockScan.BlockEnd(block, BlockSize, Count),
                    (int*)BlockTotals);
            }
        }
    }
}
