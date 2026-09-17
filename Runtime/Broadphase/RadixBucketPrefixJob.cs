using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace Ember.Collision
{
    /// <summary>
    /// 基数排序步骤 2（并行，每桶一次）：汇总该桶各分块计数，
    /// 写出桶内局部游标并将桶总数写入 <see cref="Totals"/>。
    /// 按桶并行使单桶内的 <c>blockCount</c> 次访存完全连续。
    /// </summary>
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct RadixBucketPrefixJob : IJobParallelFor
    {
        [NativeDisableUnsafePtrRestriction] public long HistogramPtr;

        [NativeDisableUnsafePtrRestriction] public long OffsetsPtr;

        [NativeDisableUnsafePtrRestriction] public long TotalsPtr;

        public int BlockCount;

        public unsafe void Execute(int bucket)
        {
            var Histogram = (int*)HistogramPtr;
            var Offsets = (int*)OffsetsPtr;
            var Totals = (int*)TotalsPtr;
            unsafe
            {
                RadixSort32.BucketLocalPrefix(
                    bucket,
                    (int*)Histogram,
                    (int*)Offsets,
                    (int*)Totals,
                    BlockCount);
            }
        }
    }
}
