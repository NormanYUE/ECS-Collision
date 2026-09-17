using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace Ember.Collision
{
    /// <summary>
    /// BVH 顶层收尾（串行，零并行度）：剩余各层宽度已很小，
    /// 合并代价可忽略，串行完成可省下 log2(阈值) 次 Job 调度。
    /// 根下标由 <see cref="BvhBuilder.RootIndex"/> 解析给出，故此处无需回写。
    /// </summary>
    [BurstCompile]
    public struct BvhFinalizeJob : IJob
    {
        [NativeDisableUnsafePtrRestriction] public long NodesPtr;

        public int LevelStart;
        public int LevelCount;
        public int ParentStart;

        public unsafe void Execute()
        {
            var Nodes = (BvhNode*)NodesPtr;
            unsafe
            {
                BvhBuilder.MergeToRoot(
                    (BvhNode*)Nodes, LevelStart, LevelCount, ParentStart);
            }
        }
    }
}
