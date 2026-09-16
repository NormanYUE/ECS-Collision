using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;

namespace Ember.Collision.Tests
{
    [TestFixture]
    public unsafe class NarrowphaseMathTests
    {
        [Test]
        public void SphereSphere_Overlapping_BuildsOneMidpointManifold()
        {
            bool hit = NarrowphaseMath.TryBuildManifold(
                Collider.Sphere(1f), PoseAt(float3.zero),
                Collider.Sphere(1f), PoseAt(new float3(1.5f, 0f, 0f)),
                CollisionDimension.XYZ, null, 0, out ContactManifold manifold);

            Assert.That(hit, Is.True);
            Assert.That(manifold.Count, Is.EqualTo(1));
            Assert.That(manifold.Normal, Is.EqualTo(new float3(1f, 0f, 0f)));
            Assert.That(manifold.P0.Separation, Is.EqualTo(-0.5f).Within(1e-5f));
            Assert.That(manifold.P0.Position, Is.EqualTo(new float3(0.75f, 0f, 0f)).Using(Float3Comparer.Within(1e-5f)));
        }

        [Test]
        public void SphereSphere_SeparatedByEpsilon_ReturnsFalse()
        {
            bool hit = NarrowphaseMath.TryBuildManifold(
                Collider.Sphere(1f), PoseAt(float3.zero),
                Collider.Sphere(1f), PoseAt(new float3(2.001f, 0f, 0f)),
                CollisionDimension.XYZ, null, 0, out _);

            Assert.That(hit, Is.False);
        }

        [Test]
        public void SphereSphere_SwappingArguments_FlipsNormalAndPreservesWitness()
        {
            Collider a = Collider.Sphere(1f);
            Collider b = Collider.Sphere(1f);
            BodyPose poseA = PoseAt(float3.zero);
            BodyPose poseB = PoseAt(new float3(1.5f, 0f, 0f));

            Assert.That(NarrowphaseMath.TryBuildManifold(
                a, poseA, b, poseB, CollisionDimension.XYZ, null, 0, out ContactManifold forward), Is.True);
            Assert.That(NarrowphaseMath.TryBuildManifold(
                b, poseB, a, poseA, CollisionDimension.XYZ, null, 0, out ContactManifold reverse), Is.True);

            Assert.That(reverse.Normal, Is.EqualTo(-forward.Normal));
            Assert.That(reverse.P0.Separation, Is.EqualTo(forward.P0.Separation).Within(1e-5f));
            Assert.That(reverse.P0.Position, Is.EqualTo(forward.P0.Position).Using(Float3Comparer.Within(1e-5f)));
        }

        [Test]
        public void SphereBox_Overlapping_UsesClosestBoxFace()
        {
            AssertSingleContact(
                Collider.Sphere(1f), PoseAt(float3.zero),
                Collider.Box(new float3(1f)), PoseAt(new float3(1.5f, 0f, 0f)),
                CollisionDimension.XYZ, new float3(1f, 0f, 0f), -0.5f);
        }

        [Test]
        public void SphereCapsule_Overlapping_UsesClosestSegmentPoint()
        {
            AssertSingleContact(
                Collider.Sphere(1f), PoseAt(float3.zero),
                Collider.Capsule(0.5f, 0f), PoseAt(new float3(1f, 0f, 0f)),
                CollisionDimension.XYZ, new float3(1f, 0f, 0f), -0.5f);
        }

        [Test]
        public void BoxBox_Overlapping_UsesSeparatingAxisNormal()
        {
            AssertSingleContact(
                Collider.Box(new float3(1f)), PoseAt(float3.zero),
                Collider.Box(new float3(1f)), PoseAt(new float3(1.5f, 0f, 0f)),
                CollisionDimension.XYZ, new float3(1f, 0f, 0f), -0.5f);
        }

        [Test]
        public void BoxCapsule_Overlapping_UsesClosestObbWitness()
        {
            AssertSingleContact(
                Collider.Box(new float3(1f)), PoseAt(float3.zero),
                Collider.Capsule(0.5f, 1f), PoseAt(new float3(1.25f, 0f, 0f)),
                CollisionDimension.XYZ, new float3(1f, 0f, 0f), -0.25f);
        }

        [Test]
        public void BoxCapsule_CapsuleInsideBox_UsesOutwardBoxToCapsuleNormal()
        {
            AssertSingleContact(
                Collider.Box(new float3(1f)), PoseAt(float3.zero),
                Collider.Capsule(0.25f, 0f), PoseAt(float3.zero),
                CollisionDimension.XYZ, new float3(1f, 0f, 0f), -1.25f);
        }

        [Test]
        public void BoxCapsule_EndpointTangentToBox_ReportsTouchingContact()
        {
            AssertSingleContact(
                Collider.Box(new float3(1f)), PoseAt(float3.zero),
                Collider.Capsule(0.5f, 1f), PoseAt(new float3(1.5f, 2f, 0f)),
                CollisionDimension.XYZ, new float3(1f, 0f, 0f), 0f);
        }

        [Test]
        public void BoxCapsule_SwappingArguments_FlipsNormalAndPreservesWitness()
        {
            AssertSwapSymmetric(
                Collider.Box(new float3(1f)), PoseAt(float3.zero),
                Collider.Capsule(0.5f, 1f), PoseAt(new float3(1.25f, 0f, 0f)),
                CollisionDimension.XYZ, null, 0);
        }

        [Test]
        public void CapsuleCapsule_Overlapping_UsesClosestSegmentPair()
        {
            AssertSingleContact(
                Collider.Capsule(0.5f, 1f), PoseAt(float3.zero),
                Collider.Capsule(0.5f, 1f), PoseAt(new float3(0.75f, 0f, 0f)),
                CollisionDimension.XYZ, new float3(1f, 0f, 0f), -0.25f);
        }

        [Test]
        public void CapsuleCapsule_ShortOverlappingSegments_DoNotCollapseToEndpoints()
        {
            bool hit = NarrowphaseMath.TryBuildManifold(
                Collider.Capsule(0f, 0.0004f), PoseAt(float3.zero),
                Collider.Capsule(0f, 0.0004f), PoseAt(new float3(0f, 0.0007f, 0f)),
                CollisionDimension.XYZ, null, 0, out ContactManifold manifold);

            Assert.That(hit, Is.True);
            Assert.That(manifold.Count, Is.EqualTo(1));
        }

        [Test]
        public void CircleCircle_Overlapping_BuildsPlanarManifold()
        {
            AssertPlanarContact(
                Collider.Circle(1f), PoseAt(float3.zero),
                Collider.Circle(1f), PoseAt(new float3(1.5f, 0f, 0f)),
                null, 0);
        }

        [Test]
        public void CircleBox2D_Overlapping_BuildsPlanarManifold()
        {
            AssertPlanarContact(
                Collider.Circle(1f), PoseAt(float3.zero),
                Collider.Box2D(new float2(1f)), PoseAt(new float3(1.5f, 0f, 0f)),
                null, 0);
        }

        [Test]
        public void CircleCapsule2D_Overlapping_BuildsPlanarManifold()
        {
            AssertPlanarContact(
                Collider.Circle(1f), PoseAt(float3.zero),
                Collider.Capsule2D(0.5f, 0f), PoseAt(new float3(1f, 0f, 0f)),
                null, 0);
        }

        [Test]
        public void CirclePolygon2D_Overlapping_BuildsPlanarManifold()
        {
            float3* vertices = stackalloc float3[4];
            WriteUnitSquare(vertices, 0);

            AssertPlanarContact(
                Collider.Circle(1f), PoseAt(float3.zero),
                Polygon(0), PoseAt(new float3(1.5f, 0f, 0f)),
                vertices, 4);
        }

        [Test]
        public void CirclePolygon2D_SwappingArguments_FlipsNormalAndPreservesWitness()
        {
            float3* vertices = stackalloc float3[4];
            WriteUnitSquare(vertices, 0);

            AssertSwapSymmetric(
                Collider.Circle(1f), PoseAt(float3.zero),
                Polygon(0), PoseAt(new float3(1.5f, 0f, 0f)),
                CollisionDimension.XY, vertices, 4);
        }

        [Test]
        public void CirclePolygon2D_Separated_ReturnsFalse()
        {
            float3* vertices = stackalloc float3[4];
            WriteUnitSquare(vertices, 0);

            bool hit = NarrowphaseMath.TryBuildManifold(
                Collider.Circle(0.5f), PoseAt(float3.zero),
                Polygon(0), PoseAt(new float3(3f, 0f, 0f)),
                CollisionDimension.XY, vertices, 4, out _);

            Assert.That(hit, Is.False);
        }

        [Test]
        public void CirclePolygon2D_XZ_UsesPlanarVertexCoordinates()
        {
            float3* vertices = stackalloc float3[4];
            WriteUnitSquareXZ(vertices, 0);

            AssertPlanarContact(
                Collider.Circle(1f), PoseAt(float3.zero),
                Polygon(0), PoseAt(new float3(1.5f, 0f, 0f)),
                vertices, 4, CollisionDimension.XZ);
        }

        [Test]
        public void Polygon2D_InvalidVertexRange_ReturnsFalse()
        {
            float3* vertices = stackalloc float3[3];
            WriteTriangle(vertices, 0);
            Collider invalid = Collider.Polygon2D(vertexStart: 1, vertexCount: 3);

            bool hit = NarrowphaseMath.TryBuildManifold(
                Collider.Circle(1f), PoseAt(float3.zero),
                invalid, PoseAt(new float3(1f, 0f, 0f)),
                CollisionDimension.XY, vertices, 3, out _);

            Assert.That(hit, Is.False);
        }

        [Test]
        public void MixedTwoAndThreeDimensionalShapes_ReturnFalse()
        {
            bool hit = NarrowphaseMath.TryBuildManifold(
                Collider.Circle(1f), PoseAt(float3.zero),
                Collider.Sphere(1f), PoseAt(float3.zero),
                CollisionDimension.XYZ, null, 0, out _);

            Assert.That(hit, Is.False);
        }

        [Test]
        public void Box2DBox2D_Overlapping_BuildsPlanarManifold()
        {
            AssertPlanarContact(
                Collider.Box2D(new float2(1f)), PoseAt(float3.zero),
                Collider.Box2D(new float2(1f)), PoseAt(new float3(1.5f, 0f, 0f)),
                null, 0);
        }

        [Test]
        public void Box2DCapsule2D_Overlapping_BuildsPlanarManifold()
        {
            AssertPlanarContact(
                Collider.Box2D(new float2(1f)), PoseAt(float3.zero),
                Collider.Capsule2D(0.5f, 1f), PoseAt(new float3(1.25f, 0f, 0f)),
                null, 0);
        }

        [Test]
        public void Box2DCapsule2D_CapsuleInsideBox_UsesOutwardBoxToCapsuleNormal()
        {
            AssertPlanarContact(
                Collider.Box2D(new float2(1f)), PoseAt(float3.zero),
                Collider.Capsule2D(0.25f, 0f), PoseAt(float3.zero),
                null, 0);
        }

        [Test]
        public void Box2DCapsule2D_EndpointTangentToBox_ReportsTouchingContact()
        {
            AssertPlanarContact(
                Collider.Box2D(new float2(1f)), PoseAt(float3.zero),
                Collider.Capsule2D(0.5f, 1f), PoseAt(new float3(1.5f, 2f, 0f)),
                null, 0);
        }

        [Test]
        public void Box2DPolygon2D_Overlapping_BuildsPlanarManifold()
        {
            float3* vertices = stackalloc float3[4];
            WriteUnitSquare(vertices, 0);

            AssertPlanarContact(
                Collider.Box2D(new float2(1f)), PoseAt(float3.zero),
                Polygon(0), PoseAt(new float3(1.5f, 0f, 0f)),
                vertices, 4);
        }

        [Test]
        public void Capsule2DCapsule2D_Overlapping_BuildsPlanarManifold()
        {
            AssertPlanarContact(
                Collider.Capsule2D(0.5f, 1f), PoseAt(float3.zero),
                Collider.Capsule2D(0.5f, 1f), PoseAt(new float3(0.75f, 0f, 0f)),
                null, 0);
        }

        [Test]
        public void Capsule2DPolygon2D_Overlapping_BuildsPlanarManifold()
        {
            float3* vertices = stackalloc float3[4];
            WriteUnitSquare(vertices, 0);

            AssertPlanarContact(
                Collider.Capsule2D(0.5f, 1f), PoseAt(float3.zero),
                Polygon(0), PoseAt(new float3(1.25f, 0f, 0f)),
                vertices, 4);
        }

        [Test]
        public void Polygon2DPolygon2D_Overlapping_BuildsPlanarManifold()
        {
            float3* vertices = stackalloc float3[8];
            WriteUnitSquare(vertices, 0);
            WriteUnitSquare(vertices, 4);

            AssertPlanarContact(
                Polygon(0), PoseAt(float3.zero),
                Polygon(4), PoseAt(new float3(1.5f, 0f, 0f)),
                vertices, 8);
        }

        [Test]
        public void CircleBox2D_XZ_UsesConfiguredPlane()
        {
            AssertPlanarContact(
                Collider.Circle(1f), PoseAt(float3.zero),
                Collider.Box2D(new float2(1f)), PoseAt(new float3(1.5f, 0f, 0f)),
                null, 0, CollisionDimension.XZ);
        }

        [Test]
        public void CircleCapsule2D_XZ_AxisVUsesZ()
        {
            AssertPlanarContact(
                Collider.Circle(0.25f), PoseAt(float3.zero),
                Collider.Capsule2D(0.25f, 1f, axis: 1), PoseAt(new float3(0f, 0f, 1.4f)),
                null, 0, CollisionDimension.XZ, new float3(0f, 0f, 1f));
        }

        [Test]
        public void CircleBox2D_XZ_SeparatedAlongZ_ReturnsFalse()
        {
            bool hit = NarrowphaseMath.TryBuildManifold(
                Collider.Circle(1f), PoseAt(float3.zero),
                Collider.Box2D(new float2(1f)), PoseAt(new float3(0f, 0f, 3f)),
                CollisionDimension.XZ, null, 0, out _);

            Assert.That(hit, Is.False);
        }

        [Test]
        public void Box2DBox2D_Rotated45Degrees_DistinguishesOverlapFromSeparation()
        {
            Collider box = Collider.Box2D(new float2(1f));
            quaternion rotation = quaternion.RotateZ(math.radians(45f));

            bool overlap = NarrowphaseMath.TryBuildManifold(
                box, PoseAt(float3.zero),
                box, PoseAt(new float3(2.3f, 0f, 0f), rotation),
                CollisionDimension.XY, null, 0, out ContactManifold manifold);
            bool separation = NarrowphaseMath.TryBuildManifold(
                box, PoseAt(float3.zero),
                box, PoseAt(new float3(2.5f, 0f, 0f), rotation),
                CollisionDimension.XY, null, 0, out _);

            Assert.That(overlap, Is.True);
            Assert.That(manifold.Normal.x, Is.GreaterThan(0.99f));
            Assert.That(separation, Is.False);
        }

        [Test]
        public void BoxBox_RotatedEdgeCrossAxis_DistinguishesOverlapFromSeparation()
        {
            Collider rod = Collider.Box(new float3(1f, 0.1f, 0.1f));
            quaternion rotation = new(
                0.17325461f, -0.2553961f, 0.38222727f, 0.87100977f);

            bool overlap = NarrowphaseMath.TryBuildManifold(
                rod, PoseAt(float3.zero),
                rod, PoseAt(new float3(0f, -0.18384777f, 0.18384777f), rotation),
                CollisionDimension.XYZ, null, 0, out _);
            bool separation = NarrowphaseMath.TryBuildManifold(
                rod, PoseAt(float3.zero),
                rod, PoseAt(new float3(0f, -0.19304015f, 0.19304015f), rotation),
                CollisionDimension.XYZ, null, 0, out _);

            Assert.That(overlap, Is.True);
            Assert.That(separation, Is.False);
        }

        private static BodyPose PoseAt(float3 position) => PoseAt(position, quaternion.identity);

        private static BodyPose PoseAt(float3 position, quaternion rotation) => new()
        {
            Position = position,
            Rotation = rotation,
            Scale = 1f,
        };

        private static unsafe void AssertSingleContact(
            Collider a,
            BodyPose poseA,
            Collider b,
            BodyPose poseB,
            CollisionDimension dimension,
            float3 expectedNormal,
            float expectedSeparation)
        {
            bool hit = NarrowphaseMath.TryBuildManifold(
                a, poseA, b, poseB, dimension, null, 0, out ContactManifold manifold);

            Assert.That(hit, Is.True);
            Assert.That(manifold.Count, Is.EqualTo(1));
            Assert.That(manifold.Normal, Is.EqualTo(expectedNormal).Using(Float3Comparer.Within(1e-5f)));
            Assert.That(math.length(manifold.Normal), Is.EqualTo(1f).Within(1e-5f));
            Assert.That(manifold.P0.Separation, Is.EqualTo(expectedSeparation).Within(1e-5f));
        }

        private static unsafe void AssertPlanarContact(
            Collider a,
            BodyPose poseA,
            Collider b,
            BodyPose poseB,
            float3* vertices,
            int vertexCount,
            CollisionDimension dimension = CollisionDimension.XY,
            float3? expectedNormal = null)
        {
            bool hit = NarrowphaseMath.TryBuildManifold(
                a, poseA, b, poseB, dimension, vertices, vertexCount, out ContactManifold manifold);

            Assert.That(hit, Is.True);
            Assert.That(manifold.Count, Is.EqualTo(1));
            Assert.That(math.length(manifold.Normal), Is.EqualTo(1f).Within(1e-5f));
            if (expectedNormal.HasValue)
                Assert.That(manifold.Normal, Is.EqualTo(expectedNormal.Value).Using(Float3Comparer.Within(1e-5f)));
            else
                Assert.That(manifold.Normal.x, Is.GreaterThan(0.99f));
            Assert.That(manifold.P0.Separation, Is.LessThanOrEqualTo(0f));
        }

        private static unsafe void AssertSwapSymmetric(
            Collider a,
            BodyPose poseA,
            Collider b,
            BodyPose poseB,
            CollisionDimension dimension,
            float3* vertices,
            int vertexCount)
        {
            Assert.That(NarrowphaseMath.TryBuildManifold(
                a, poseA, b, poseB, dimension, vertices, vertexCount, out ContactManifold forward), Is.True);
            Assert.That(NarrowphaseMath.TryBuildManifold(
                b, poseB, a, poseA, dimension, vertices, vertexCount, out ContactManifold reverse), Is.True);

            Assert.That(reverse.Normal, Is.EqualTo(-forward.Normal).Using(Float3Comparer.Within(1e-5f)));
            Assert.That(reverse.P0.Separation, Is.EqualTo(forward.P0.Separation).Within(1e-5f));
            Assert.That(reverse.P0.Position, Is.EqualTo(forward.P0.Position).Using(Float3Comparer.Within(1e-5f)));
        }

        private static Collider Polygon(int vertexStart) =>
            Collider.Polygon2D(vertexStart, 4);

        private static unsafe void WriteUnitSquare(float3* vertices, int start)
        {
            vertices[start] = new float3(-1f, -1f, 0f);
            vertices[start + 1] = new float3(1f, -1f, 0f);
            vertices[start + 2] = new float3(1f, 1f, 0f);
            vertices[start + 3] = new float3(-1f, 1f, 0f);
        }

        private static unsafe void WriteUnitSquareXZ(float3* vertices, int start)
        {
            vertices[start] = new float3(-1f, 0f, -1f);
            vertices[start + 1] = new float3(1f, 0f, -1f);
            vertices[start + 2] = new float3(1f, 0f, 1f);
            vertices[start + 3] = new float3(-1f, 0f, 1f);
        }

        private static unsafe void WriteTriangle(float3* vertices, int start)
        {
            vertices[start] = new float3(-1f, -1f, 0f);
            vertices[start + 1] = new float3(1f, -1f, 0f);
            vertices[start + 2] = new float3(0f, 1f, 0f);
        }

        private sealed class Float3Comparer : IEqualityComparer<float3>
        {
            private readonly float m_Tolerance;

            private Float3Comparer(float tolerance)
            {
                m_Tolerance = tolerance;
            }

            public static Float3Comparer Within(float tolerance) => new(tolerance);

            public bool Equals(float3 x, float3 y) => math.lengthsq(x - y) <= m_Tolerance * m_Tolerance;

            public int GetHashCode(float3 value) => 0;
        }
    }
}
