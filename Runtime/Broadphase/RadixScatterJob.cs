using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace Ember.Collision
{
    /// <summary>
    /// 基数排序步骤 4（并行，每分块一次）：把本分块元素按当前位散布到目标数组，
    /// 键与下标成对置换。同分块内按输入顺序写入同一桶的递增位置，故排序<b>稳定</b>
    /// ——这是宽相 pair 顺序确定（可回放、可对拍）的前提。
    /// </summary>
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct RadixScatterJob : IJobParallelFor
    {
        [NativeDisableUnsafePtrRestriction] public long KeysPtr;

        [NativeDisableUnsafePtrRestriction] public long OrderPtr;

        [NativeDisableUnsafePtrRestriction] public long OffsetsPtr;

        [NativeDisableUnsafePtrRestriction] public long TotalsPtr;

        [NativeDisableUnsafePtrRestriction] public long OutKeysPtr;

        [NativeDisableUnsafePtrRestriction] public long OutOrderPtr;

        public int Count;
        public int BlockSize;
        public int BlockCount;
        public int Shift;

        public unsafe void Execute(int block)
        {
            var Keys = (uint*)KeysPtr;
            var Order = (int*)OrderPtr;
            var Offsets = (int*)OffsetsPtr;
            var Totals = (int*)TotalsPtr;
            var OutKeys = (uint*)OutKeysPtr;
            var OutOrder = (int*)OutOrderPtr;
            unsafe
            {
                RadixSort32.ScatterBlock(
                    (uint*)Keys,
                    (int*)Order,
                    (uint*)OutKeys,
                    (int*)OutOrder,
                    block,
                    RadixSort32.BlockStart(block, BlockSize),
                    RadixSort32.BlockEnd(block, BlockSize, Count),
                    Shift,
                    (int*)Offsets,
                    (int*)Totals,
                    BlockCount);
            }
        }
    }
}
