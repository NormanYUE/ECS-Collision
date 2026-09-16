using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace Ember.Collision
{
    /// <summary>
    /// 把 per-pair 命中归约为 per-body 标志。此 Job 故意串行：同一 body 可出现在
    /// 多个 candidate pair 中，串行归约避免以禁用 safety restriction 掩盖共享写入竞争。
    /// </summary>
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct ContactFlagMarkJob : IJob
    {
        [ReadOnly] public NativeArray<CandidatePair> Pairs;

        [ReadOnly] public NativeArray<int> ContactCounts;

        public NativeArray<byte> BodyContactFlags;

        public int PairCount;
        public int BodyCount;

        public void Execute()
        {
            int limit = PairCount;
            if (limit > Pairs.Length) limit = Pairs.Length;
            if (limit > ContactCounts.Length) limit = ContactCounts.Length;

            for (int pairIndex = 0; pairIndex < limit; pairIndex++)
            {
                if (ContactCounts[pairIndex] <= 0) continue;

                CandidatePair pair = Pairs[pairIndex];
                if (pair.BodyA >= 0 && pair.BodyA < BodyCount)
                    BodyContactFlags[pair.BodyA] = 1;
                if (pair.BodyB >= 0 && pair.BodyB < BodyCount)
                    BodyContactFlags[pair.BodyB] = 1;
            }
        }
    }
}
