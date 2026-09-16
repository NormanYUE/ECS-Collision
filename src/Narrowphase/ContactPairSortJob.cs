using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace Ember.Collision
{
    /// <summary>稳定排序本帧 pair 历史，供 P3 线性归并。</summary>
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public unsafe struct ContactPairSortJob : IJob
    {
        public NativeArray<ContactPairRecord> Records;
        public NativeArray<ContactPairRecord> Scratch;
        public int Count;

        public void Execute()
        {
            ContactEventMath.Sort(
                (ContactPairRecord*)Records.GetUnsafePtr(),
                (ContactPairRecord*)Scratch.GetUnsafePtr(),
                Count);
        }
    }
}
