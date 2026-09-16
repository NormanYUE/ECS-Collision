using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace Ember.Collision
{
    /// <summary>在窄相计数前清除稠密 body 的本帧接触标志。</summary>
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct ContactFlagClearJob : IJobParallelFor
    {
        public NativeArray<byte> BodyContactFlags;

        public void Execute(int bodyIndex)
        {
            BodyContactFlags[bodyIndex] = 0;
        }
    }
}
