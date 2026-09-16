using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace Ember.Collision
{
    /// <summary>
    /// 将 PairCountJob 的负溢出哨兵归零，并在唯一串行写入点汇总诊断。
    /// 必须在前缀和之前执行，避免负值污染 pair offset。
    /// </summary>
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct PairCountNormalizeJob : IJob
    {
        public NativeArray<int> PairCounts;

        public NativeArray<int> DiagnosticFlags;

        public int BodyCount;
        public int StackOverflowSlot;

        public void Execute()
        {
            bool overflow = false;
            for (int leafIndex = 0; leafIndex < BodyCount; leafIndex++)
            {
                if (PairCounts[leafIndex] >= 0) continue;
                PairCounts[leafIndex] = 0;
                overflow = true;
            }

            if (overflow)
                DiagnosticFlags[StackOverflowSlot] = 1;
        }
    }
}
