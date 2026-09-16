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
        [ReadOnly] public NativeArray<CandidatePair> Pairs;
        [ReadOnly] public NativeArray<Entity> BodyEntities;
        [ReadOnly] public NativeArray<int> ContactCounts;
        [ReadOnly] public NativeArray<int> ContactOffsets;

        [NativeDisableParallelForRestriction] public NativeArray<ContactPairRecord> Output;
        public int BodyCount;

        public void Execute(int pairIndex)
        {
            if (ContactCounts[pairIndex] <= 0) return;
            CandidatePair pair = Pairs[pairIndex];
            if (pair.BodyA < 0 || pair.BodyB < 0 || pair.BodyA >= BodyCount || pair.BodyB >= BodyCount) return;

            int output = ContactOffsets[pairIndex];
            if (output < 0 || output >= Output.Length) return;
            Output[output] = ContactPairRecord.Create(BodyEntities[pair.BodyA], BodyEntities[pair.BodyB]);
        }
    }
}
