using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Collections.LowLevel.Unsafe;

namespace Ember.Collision
{
    /// <summary>
    /// Morton 编码作业（并行）：由当帧全局 AABB 构造量化坐标系，
    /// 为每个稠密 body 编码空间键，同时把排序下标数组初始化为恒等置换。
    /// </summary>
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct MortonJob : IJobParallelFor
    {
        [NativeDisableUnsafePtrRestriction] public long BodyBoundsPtr;

        /// <summary>分块归约结果；下标 <see cref="BlockCount"/> 存放全局 AABB。</summary>
        [NativeDisableUnsafePtrRestriction] public long BlockBoundsPtr;

        public int BlockCount;

        /// <summary>维度模式，决定参与量化的轴。</summary>
        public CollisionDimension Dimension;

        [NativeDisableUnsafePtrRestriction] public long MortonKeysPtr;

        [NativeDisableUnsafePtrRestriction] public long BodyOrderPtr;

        public unsafe void Execute(int index)
        {
            var BodyBounds = (Aabb*)BodyBoundsPtr;
            var BlockBounds = (Aabb*)BlockBoundsPtr;
            var MortonKeys = (uint*)MortonKeysPtr;
            var BodyOrder = (int*)BodyOrderPtr;
            MortonFrame frame = MortonFrame.From(BlockBounds[BlockCount], Dimension);
            MortonKeys[index] = MortonCoder.Encode(BodyBounds[index], frame);
            BodyOrder[index] = index;
        }
    }
}
