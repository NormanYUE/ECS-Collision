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
        [ReadOnly] public NativeArray<uint> Keys;

        [ReadOnly] public NativeArray<int> Order;

        [NativeDisableParallelForRestriction] public NativeArray<int> Offsets;

        [NativeDisableParallelForRestriction] public NativeArray<int> Totals;

        [NativeDisableParallelForRestriction] public NativeArray<uint> OutKeys;

        [NativeDisableParallelForRestriction] public NativeArray<int> OutOrder;

        public int Count;
        public int BlockSize;
        public int BlockCount;
        public int Shift;

        public void Execute(int block)
        {
            unsafe
            {
                RadixSort32.ScatterBlock(
                    (uint*)Keys.GetUnsafeReadOnlyPtr(),
                    (int*)Order.GetUnsafeReadOnlyPtr(),
                    (uint*)OutKeys.GetUnsafePtr(),
                    (int*)OutOrder.GetUnsafePtr(),
                    block,
                    RadixSort32.BlockStart(block, BlockSize),
                    RadixSort32.BlockEnd(block, BlockSize, Count),
                    Shift,
                    (int*)Offsets.GetUnsafePtr(),
                    (int*)Totals.GetUnsafePtr(),
                    BlockCount);
            }
        }
    }
}
