using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace Ember.Collision
{
    /// <summary>
    /// 基数排序步骤 1（并行，每分块一次）：统计本分块各桶计数。
    /// 逻辑委托给纯函数核心 <see cref="RadixSort32.HistogramBlock"/>，保证
    /// Job 与 CLI 单测跑的是同一份实现。
    /// </summary>
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct RadixHistogramJob : IJobParallelFor
    {
        [NativeDisableUnsafePtrRestriction] public long KeysPtr;

        [NativeDisableUnsafePtrRestriction] public long HistogramPtr;

        public int Count;
        public int BlockSize;
        public int BlockCount;
        public int Shift;

        public unsafe void Execute(int block)
        {
            var Keys = (uint*)KeysPtr;
            var Histogram = (int*)HistogramPtr;
            unsafe
            {
                RadixSort32.HistogramBlock(
                    (uint*)Keys,
                    block,
                    RadixSort32.BlockStart(block, BlockSize),
                    RadixSort32.BlockEnd(block, BlockSize, Count),
                    Shift,
                    (int*)Histogram,
                    BlockCount);
            }
        }
    }
}
