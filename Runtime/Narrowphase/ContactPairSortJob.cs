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
        [NativeDisableUnsafePtrRestriction] public long RecordsPtr;
        [NativeDisableUnsafePtrRestriction] public long ScratchPtr;
        public int Count;

        public unsafe void Execute()
        {
            var Records = (ContactPairRecord*)RecordsPtr;
            var Scratch = (ContactPairRecord*)ScratchPtr;
            ContactEventMath.Sort(
                (ContactPairRecord*)Records,
                (ContactPairRecord*)Scratch,
                Count);
        }
    }
}
