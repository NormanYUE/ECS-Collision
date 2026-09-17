using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Collections.LowLevel.Unsafe;

namespace Ember.Collision
{
    /// <summary>在窄相计数前清除稠密 body 的本帧接触标志。</summary>
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct ContactFlagClearJob : IJobParallelFor
    {
        [NativeDisableUnsafePtrRestriction] public long BodyContactFlagsPtr;

        public unsafe void Execute(int bodyIndex)
        {
            var BodyContactFlags = (byte*)BodyContactFlagsPtr;
            BodyContactFlags[bodyIndex] = 0;
        }
    }
}
