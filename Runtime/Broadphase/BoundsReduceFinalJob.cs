using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Collections.LowLevel.Unsafe;

namespace Ember.Collision
{
    /// <summary>
    /// 分块包围盒归约第二步（串行，仅 blockCount 次迭代）：
    /// 把各块并集合并为全局 AABB，写入 <c>BlockBounds[blockCount]</c>，
    /// 作为 Morton 量化坐标系的基准（索引即当帧量化范围）。
    /// </summary>
    [BurstCompile]
    public struct BoundsReduceFinalJob : IJob
    {
        [NativeDisableUnsafePtrRestriction] public long BlockBoundsPtr;

        public int BlockCount;

        public unsafe void Execute()
        {
            var BlockBounds = (Aabb*)BlockBoundsPtr;
            Aabb total = Aabb.Empty;
            for (int i = 0; i < BlockCount; i++)
                total.Encapsulate(BlockBounds[i]);

            // 退化场景（单点 / 全部同位置）时给一个最小尺寸，避免量化范围为零。
            if (total.IsEmpty)
                total = Aabb.FromCenterExtents(Unity.Mathematics.float3.zero, new Unity.Mathematics.float3(1f));

            BlockBounds[BlockCount] = total;
        }
    }
}
