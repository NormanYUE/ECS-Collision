using Ember;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace Ember.Collision
{
    /// <summary>按未截断的 contact offset 收集 P3 pair 历史。</summary>
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct ContactPairGatherJob : IJobParallelFor
    {
        [NativeDisableUnsafePtrRestriction] public long PairsPtr;
        [NativeDisableUnsafePtrRestriction] public long BodyEntitiesPtr;
        [NativeDisableUnsafePtrRestriction] public long ContactCountsPtr;
        [NativeDisableUnsafePtrRestriction] public long ContactOffsetsPtr;

        [NativeDisableUnsafePtrRestriction] public long OutputPtr;
        public int BodyCount;
        public int OutputCapacity;

        public unsafe void Execute(int pairIndex)
        {
            var Pairs = (CandidatePair*)PairsPtr;
            var BodyEntities = (Entity*)BodyEntitiesPtr;
            var ContactCounts = (int*)ContactCountsPtr;
            var ContactOffsets = (int*)ContactOffsetsPtr;
            var Output = (ContactPairRecord*)OutputPtr;
            if (ContactCounts[pairIndex] <= 0) return;
            CandidatePair pair = Pairs[pairIndex];
            if (pair.BodyA < 0 || pair.BodyB < 0 || pair.BodyA >= BodyCount || pair.BodyB >= BodyCount) return;

            int output = ContactOffsets[pairIndex];
            if (output < 0 || output >= OutputCapacity) return;
            Output[output] = ContactPairRecord.Create(BodyEntities[pair.BodyA], BodyEntities[pair.BodyB]);
        }
    }
}
