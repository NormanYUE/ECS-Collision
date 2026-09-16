using Ember;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace Ember.Collision
{
    /// <summary>
    /// 窄相第二阶段：按前缀和给出的唯一 offset 写入流形。
    /// 几何内核会被确定性地重算，避免为 count 阶段保留每 pair 的临时流形。
    /// </summary>
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public unsafe struct ContactCollectJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<CandidatePair> Pairs;

        [ReadOnly] public NativeArray<Entity> BodyEntities;

        [ReadOnly] public NativeArray<BodyPose> BodyPoses;

        [ReadOnly] public NativeArray<Collider> BodyColliders;

        [ReadOnly] public NativeArray<CollisionFilter> BodyFilters;

        [ReadOnly] public NativeArray<byte> BodyFlags;

        [ReadOnly] public NativeArray<float3> Vertices;

        [ReadOnly] public NativeArray<int> ContactCounts;

        [ReadOnly] public NativeArray<int> ContactOffsets;

        [NativeDisableParallelForRestriction] public NativeArray<ContactManifold> Contacts;

        public int BodyCount;
        public int VertexCount;
        public int OutputLimit;
        public CollisionDimension Dimension;
        public bool SkipStaticPairs;

        public void Execute(int pairIndex)
        {
            if (pairIndex < 0 || pairIndex >= Pairs.Length || ContactCounts[pairIndex] <= 0) return;

            int output = ContactOffsets[pairIndex];
            if (output < 0 || output >= OutputLimit || output >= Contacts.Length)
                return;

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
                    Dimension, vertexPool, VertexCount, out ContactManifold manifold))
                return;

            manifold.A = BodyEntities[bodyA];
            manifold.B = BodyEntities[bodyB];
            Contacts[output] = manifold;
        }
    }
}
