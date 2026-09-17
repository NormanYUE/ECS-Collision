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
        [NativeDisableUnsafePtrRestriction] public long PairsPtr;

        [NativeDisableUnsafePtrRestriction] public long BodyEntitiesPtr;

        [NativeDisableUnsafePtrRestriction] public long BodyPosesPtr;

        [NativeDisableUnsafePtrRestriction] public long BodyCollidersPtr;

        [NativeDisableUnsafePtrRestriction] public long BodyFiltersPtr;

        [NativeDisableUnsafePtrRestriction] public long BodyFlagsPtr;

        [NativeDisableUnsafePtrRestriction] public long VerticesPtr;

        [NativeDisableUnsafePtrRestriction] public long ContactCountsPtr;

        [NativeDisableUnsafePtrRestriction] public long ContactOffsetsPtr;

        [NativeDisableUnsafePtrRestriction] public long ContactsPtr;

        public int BodyCount;
        public int VertexCount;
        public int OutputLimit;
        public int PairCapacity;
        public int ContactCapacity;
        public CollisionDimension Dimension;
        public bool SkipStaticPairs;

        public unsafe void Execute(int pairIndex)
        {
            var Pairs = (CandidatePair*)PairsPtr;
            var BodyEntities = (Entity*)BodyEntitiesPtr;
            var BodyPoses = (BodyPose*)BodyPosesPtr;
            var BodyColliders = (Collider*)BodyCollidersPtr;
            var BodyFilters = (CollisionFilter*)BodyFiltersPtr;
            var BodyFlags = (byte*)BodyFlagsPtr;
            var Vertices = (float3*)VerticesPtr;
            var ContactCounts = (int*)ContactCountsPtr;
            var ContactOffsets = (int*)ContactOffsetsPtr;
            var Contacts = (ContactManifold*)ContactsPtr;
            if (pairIndex < 0 || pairIndex >= PairCapacity || ContactCounts[pairIndex] <= 0) return;

            int output = ContactOffsets[pairIndex];
            if (output < 0 || output >= OutputLimit || output >= ContactCapacity)
                return;

            CandidatePair pair = Pairs[pairIndex];
            int bodyA = pair.BodyA;
            int bodyB = pair.BodyB;
            if (bodyA < 0 || bodyB < 0 || bodyA >= BodyCount || bodyB >= BodyCount) return;
            if (!NarrowphaseMath.IsPairEligible(
                    BodyFilters[bodyA], BodyFlags[bodyA],
                    BodyFilters[bodyB], BodyFlags[bodyB], SkipStaticPairs))
                return;

            float3* vertexPool = VertexCount > 0 && Vertices != null
                ? (float3*)Vertices
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
