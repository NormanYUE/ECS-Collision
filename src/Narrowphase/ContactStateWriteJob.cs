using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace Ember.Collision
{
    /// <summary>
    /// 每 Chunk 独占写回接触历史。count Job 只写 World scratch，避免多个 pair
    /// 竞争同一 ECS 列；本 Job 让每条 CollisionState 列仅由所属 Chunk 的任务访问。
    /// </summary>
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public unsafe struct ContactStateWriteJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<ChunkInfo> ChunkInfos;

        [ReadOnly] public NativeArray<byte> BodyContactFlags;

        public void Execute(int chunkIndex)
        {
            ChunkInfo info = ChunkInfos[chunkIndex];
            if (info.Count <= 0 || info.StatePtr == 0) return;

            var states = (CollisionState*)info.StatePtr;
            for (int row = 0; row < info.Count; row++)
            {
                CollisionState state = states[row];
                state.SetFrameContact(BodyContactFlags[info.Base + row] != 0);
                states[row] = state;
            }
        }
    }
}
