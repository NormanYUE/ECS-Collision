using NUnit.Framework;
using Unity.Mathematics;

namespace Ember.Collision.Tests
{
    [TestFixture]
    public class CollisionQueryMathTests
    {
        [Test]
        public void TryRayAabb_OutsideTowardFace_ReturnsEntryDistanceAndNormal()
        {
            Aabb box = Aabb.FromCenterExtents(float3.zero, new float3(1f));

            bool hit = CollisionQueryMath.TryRayAabb(
                new float3(-3f, 0f, 0f), new float3(1f, 0f, 0f), 10f, box,
                out float distance, out float3 normal);

            Assert.That(hit, Is.True);
            Assert.That(distance, Is.EqualTo(2f).Within(1e-5f));
            Assert.That(normal, Is.EqualTo(new float3(-1f, 0f, 0f)));
        }

        [Test]
        public void TryRayAabb_StartInside_ReturnsZeroAndOpposesDirection()
        {
            Aabb box = Aabb.FromCenterExtents(float3.zero, new float3(1f));

            bool hit = CollisionQueryMath.TryRayAabb(
                float3.zero, new float3(1f, 0f, 0f), 10f, box,
                out float distance, out float3 normal);

            Assert.That(hit, Is.True);
            Assert.That(distance, Is.EqualTo(0f));
            Assert.That(normal, Is.EqualTo(new float3(-1f, 0f, 0f)));
        }

        [Test]
        public void TryRayAabb_ParallelOutsideSlab_ReturnsFalse()
        {
            Aabb box = Aabb.FromCenterExtents(float3.zero, new float3(1f));

            bool hit = CollisionQueryMath.TryRayAabb(
                new float3(0f, 2f, 0f), new float3(1f, 0f, 0f), 10f, box,
                out _, out _);

            Assert.That(hit, Is.False);
        }
    }
}
