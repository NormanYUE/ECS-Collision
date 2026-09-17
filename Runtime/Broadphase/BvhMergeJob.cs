using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace Ember.Collision
{
    /// <summary>
    /// BVH 单层合并（并行，<b>每父节点一次</b>执行）。
    /// 补足到 2 的幂后每层节点数都是偶数，因此不含「奇数尾节点」退化分支——
    /// 这是「左子树 × 右子树」pair 规则不会产生自配对 / 重复 pair 的前提。
    /// </summary>
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct BvhMergeJob : IJobParallelFor
    {
        [NativeDisableUnsafePtrRestriction] public long NodesPtr;

        public int LevelStart;
        public int ParentStart;

        public unsafe void Execute(int parentIndex)
        {
            var Nodes = (BvhNode*)NodesPtr;
            unsafe
            {
                BvhBuilder.MergeParent(
                    (BvhNode*)Nodes, LevelStart, ParentStart, parentIndex);
            }
        }
    }
}
