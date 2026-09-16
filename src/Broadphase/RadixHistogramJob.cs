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
        [ReadOnly] public NativeArray<uint> Keys;

        [NativeDisableParallelForRestriction] public NativeArray<int> Histogram;

        public int Count;
        public int BlockSize;
        public int BlockCount;
        public int Shift;

        public void Execute(int block)
        {
            unsafe
            {
                RadixSort32.HistogramBlock(
                    (uint*)Keys.GetUnsafeReadOnlyPtr(),
                    block,
                    RadixSort32.BlockStart(block, BlockSize),
                    RadixSort32.BlockEnd(block, BlockSize, Count),
                    Shift,
                    (int*)Histogram.GetUnsafePtr(),
                    BlockCount);
            }
        }
    }
}
