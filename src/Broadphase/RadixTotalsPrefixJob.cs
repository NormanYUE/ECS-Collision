using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace Ember.Collision
{
    /// <summary>
    /// 基数排序步骤 3（串行，仅 256 次操作）：把桶总数原地前缀和为各桶全局起始。
    /// 串行成本与实体数无关，因此不需要并行扫描。
    /// </summary>
    [BurstCompile]
    public struct RadixTotalsPrefixJob : IJob
    {
        [NativeDisableParallelForRestriction] public NativeArray<int> Totals;

        public void Execute()
        {
            unsafe
            {
                RadixSort32.PrefixTotals((int*)Totals.GetUnsafePtr());
            }
        }
    }
}
