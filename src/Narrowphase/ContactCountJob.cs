using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace Ember.Collision
{
    /// <summary>
    /// 窄相第一阶段：每个候选 pair 独立判定，写出 0/1 个流形。
    /// 后续前缀和据此给每个命中 pair 分配确定性的输出位置。
    /// </summary>
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public unsafe struct ContactCountJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<CandidatePair> Pairs;

        [ReadOnly] public NativeArray<BodyPose> BodyPoses;

        [ReadOnly] public NativeArray<Collider> BodyColliders;

        [ReadOnly] public NativeArray<CollisionFilter> BodyFilters;

        [ReadOnly] public NativeArray<byte> BodyFlags;

        [ReadOnly] public NativeArray<float3> Vertices;

        [NativeDisableParallelForRestriction] public NativeArray<int> ContactCounts;

        public int BodyCount;
        public int VertexCount;
        public CollisionDimension Dimension;
        public bool SkipStaticPairs;

        public void Execute(int pairIndex)
        {
            ContactCounts[pairIndex] = 0;
            if (pairIndex < 0 || pairIndex >= Pairs.Length) return;

            CandidatePair pair = Pairs[pairIndex];
            int bodyA = pair.BodyA;
            int bodyB = pair.BodyB;
            if (bodyA < 0 || bodyB < 0 || bodyA >= BodyCount || bodyB >= BodyCount) return;
            if (!NarrowphaseMath.IsPairEligible(
                    BodyFilters[bodyA], BodyFlags[bodyA],
                    BodyFilters[bodyB], BodyFlags[bodyB], SkipStaticPairs))
                return;

            float3* vertexPool = VertexCount > 0 && Vertices.IsCreated
                ? (float3*)Vertices.GetUnsafeReadOnlyPtr()
                : null;
            if (!NarrowphaseMath.TryBuildManifold(
                BodyColliders[bodyA], BodyPoses[bodyA],
                BodyColliders[bodyB], BodyPoses[bodyB],
                Dimension, vertexPool, VertexCount, out _))
                return;

            ContactCounts[pairIndex] = 1;
        }
    }
}
