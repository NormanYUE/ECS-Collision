using Ember.Collision;
using NUnit.Framework;
using Unity.Mathematics;

namespace Ember.Collision.Tests
{
    /// <summary>
    /// ShapeQuery（N1）解析解对拍：每种形状覆盖外部 / 内部 / 表面三种情形，
    /// 外加退化输入与 XY/XZ 维度一致性。
    /// </summary>
    [TestFixture]
    public class ShapeQueryTests
    {
        private const float Tol = 1e-4f;

        private static void AssertVec3(float3 actual, float3 expected, string what)
        {
            Assert.That(math.distance(actual, expected), Is.LessThan(Tol), $"{what}: got {actual}, want {expected}");
        }

        // ---- Sphere ----

        [Test]
        public void Sphere_OutsidePoint_ReturnsPositiveDistance()
        {
            var collider = Collider.Sphere(radius: 2f, center: new float3(1f, 2f, 3f));
            float d = ShapeQuery.ClosestPoint(collider, BodyPose.Identity, CollisionDimension.XYZ,
                new float3(5f, 2f, 3f), out float3 closest, out float3 normal);

            Assert.That(d, Is.EqualTo(2f).Within(Tol));
            AssertVec3(closest, new float3(3f, 2f, 3f), "closest");
            AssertVec3(normal, new float3(1f, 0f, 0f), "normal");
        }

        [Test]
        public void Sphere_InsidePoint_ReturnsNegativeDistance()
        {
            var collider = Collider.Sphere(radius: 2f, center: new float3(1f, 2f, 3f));
            float d = ShapeQuery.ClosestPoint(collider, BodyPose.Identity, CollisionDimension.XYZ,
                new float3(1f, 2f, 2f), out float3 closest, out float3 normal);

            Assert.That(d, Is.EqualTo(-1f).Within(Tol));
            AssertVec3(closest, new float3(1f, 2f, 1f), "closest");
            AssertVec3(normal, new float3(0f, 0f, -1f), "normal");
        }

        [Test]
        public void Sphere_OnSurface_ReturnsZero()
        {
            var collider = Collider.Sphere(radius: 2f, center: new float3(1f, 2f, 3f));
            float d = ShapeQuery.ClosestPoint(collider, BodyPose.Identity, CollisionDimension.XYZ,
                new float3(1f, 2f, 5f), out _, out _);

            Assert.That(d, Is.EqualTo(0f).Within(Tol));
        }

        [Test]
        public void Sphere_ZeroRadiusAtQueryPoint_ReturnsZeroWithFallbackNormal()
        {
            var collider = Collider.Sphere(radius: 0f, center: float3.zero);
            float d = ShapeQuery.ClosestPoint(collider, BodyPose.Identity, CollisionDimension.XYZ,
                float3.zero, out float3 closest, out float3 normal);

            Assert.That(d, Is.EqualTo(0f).Within(Tol));
            AssertVec3(closest, float3.zero, "closest");
            AssertVec3(normal, new float3(1f, 0f, 0f), "fallback normal");
        }

        // ---- Capsule (3D) ----

        [Test]
        public void Capsule_RadialOutside_ReturnsDistanceToSurface()
        {
            var collider = Collider.Capsule(radius: 0.5f, halfHeight: 1f, axis: 1);
            float d = ShapeQuery.ClosestPoint(collider, BodyPose.Identity, CollisionDimension.XYZ,
                new float3(3f, 0f, 0f), out float3 closest, out float3 normal);

            Assert.That(d, Is.EqualTo(2.5f).Within(Tol));
            AssertVec3(closest, new float3(0.5f, 0f, 0f), "closest");
            AssertVec3(normal, new float3(1f, 0f, 0f), "normal");
        }

        [Test]
        public void Capsule_Inside_ReturnsNegativeDistance()
        {
            var collider = Collider.Capsule(radius: 0.5f, halfHeight: 1f, axis: 1);
            float d = ShapeQuery.ClosestPoint(collider, BodyPose.Identity, CollisionDimension.XYZ,
                new float3(0.2f, 0.2f, 0f), out float3 closest, out float3 normal);

            Assert.That(d, Is.EqualTo(-0.3f).Within(Tol));
            AssertVec3(closest, new float3(0.5f, 0.2f, 0f), "closest");
            AssertVec3(normal, new float3(1f, 0f, 0f), "normal");
        }

        [Test]
        public void Capsule_EndcapOutside_ReturnsDistanceToCap()
        {
            var collider = Collider.Capsule(radius: 0.5f, halfHeight: 1f, axis: 1);
            float d = ShapeQuery.ClosestPoint(collider, BodyPose.Identity, CollisionDimension.XYZ,
                new float3(0f, 4f, 0f), out float3 closest, out float3 normal);

            Assert.That(d, Is.EqualTo(2.5f).Within(Tol));
            AssertVec3(closest, new float3(0f, 1.5f, 0f), "closest");
            AssertVec3(normal, new float3(0f, 1f, 0f), "normal");
        }

        [Test]
        public void Capsule_DegenerateZeroHalfHeight_BehavesLikeSphere()
        {
            var collider = Collider.Capsule(radius: 2f, halfHeight: 0f, axis: 1, center: new float3(1f, 2f, 3f));
            float d = ShapeQuery.ClosestPoint(collider, BodyPose.Identity, CollisionDimension.XYZ,
                new float3(5f, 2f, 3f), out float3 closest, out _);

            Assert.That(d, Is.EqualTo(2f).Within(Tol));
            AssertVec3(closest, new float3(3f, 2f, 3f), "closest");
        }

        // ---- Box (3D) ----

        [Test]
        public void Box_OutsidePoint_ReturnsDistanceToFace()
        {
            var collider = Collider.Box(halfExtents: new float3(1f, 2f, 3f));
            float d = ShapeQuery.ClosestPoint(collider, BodyPose.Identity, CollisionDimension.XYZ,
                new float3(5f, 1f, 0f), out float3 closest, out float3 normal);

            Assert.That(d, Is.EqualTo(4f).Within(Tol));
            AssertVec3(closest, new float3(1f, 1f, 0f), "closest");
            AssertVec3(normal, new float3(1f, 0f, 0f), "normal");
        }

        [Test]
        public void Box_InsidePoint_PushesOutShallowestAxis()
        {
            var collider = Collider.Box(halfExtents: new float3(1f, 2f, 3f));
            float d = ShapeQuery.ClosestPoint(collider, BodyPose.Identity, CollisionDimension.XYZ,
                new float3(0.5f, 0f, 0f), out float3 closest, out float3 normal);

            Assert.That(d, Is.EqualTo(-0.5f).Within(Tol));
            AssertVec3(closest, new float3(1f, 0f, 0f), "closest");
            AssertVec3(normal, new float3(1f, 0f, 0f), "normal");
        }

        [Test]
        public void Box_RotatedScaledPose_RespectsTransform()
        {
            // 绕 Z 转 90°：(0,1,0) → (-1,0,0)；缩放 2；平移 (10,0,0)。
            var pose = new BodyPose
            {
                Position = new float3(10f, 0f, 0f),
                Rotation = quaternion.AxisAngle(new float3(0f, 0f, 1f), math.PI * 0.5f),
                Scale = 2f,
            };
            var collider = Collider.Box(halfExtents: new float3(1f, 1f, 1f));
            float d = ShapeQuery.ClosestPoint(collider, pose, CollisionDimension.XYZ,
                new float3(10f, 5f, 0f), out float3 closest, out float3 normal);

            // 世界 +Y 对应形状的局部 +X（最近面是局部 +X 面），不是局部 +Y。
            Assert.That(d, Is.EqualTo(3f).Within(Tol));
            AssertVec3(closest, new float3(10f, 2f, 0f), "closest");
            AssertVec3(normal, new float3(0f, 1f, 0f), "normal (local +X face points world +Y)");
        }

        [Test]
        public void Box_ZeroHalfExtents_BehavesLikePoint()
        {
            var collider = Collider.Box(halfExtents: float3.zero, center: new float3(1f, 0f, 0f));
            float d = ShapeQuery.ClosestPoint(collider, BodyPose.Identity, CollisionDimension.XYZ,
                new float3(4f, 0f, 0f), out float3 closest, out _);

            Assert.That(d, Is.EqualTo(3f).Within(Tol));
            AssertVec3(closest, new float3(1f, 0f, 0f), "closest");
        }

        // ---- Circle (2D) ----

        [Test]
        public void Circle_OutsidePoint_XY()
        {
            var collider = Collider.Circle(radius: 1f);
            float d = ShapeQuery.ClosestPoint(collider, BodyPose.Identity, CollisionDimension.XY,
                new float3(3f, 0f, 7f), out float3 closest, out float3 normal);

            Assert.That(d, Is.EqualTo(2f).Within(Tol));
            AssertVec3(closest, new float3(1f, 0f, 7f), "closest (lifted at query Z)");
            AssertVec3(normal, new float3(1f, 0f, 0f), "normal");
        }

        [Test]
        public void Circle_XZDimension_MatchesXYSemantics()
        {
            var collider = Collider.Circle(radius: 1f);
            float d = ShapeQuery.ClosestPoint(collider, BodyPose.Identity, CollisionDimension.XZ,
                new float3(3f, 5f, 0f), out float3 closest, out float3 normal);

            Assert.That(d, Is.EqualTo(2f).Within(Tol));
            AssertVec3(closest, new float3(1f, 5f, 0f), "closest (lifted at query Y)");
            AssertVec3(normal, new float3(1f, 0f, 0f), "normal");
        }

        [Test]
        public void Circle_InsidePoint_ReturnsNegative()
        {
            var collider = Collider.Circle(radius: 1f);
            float d = ShapeQuery.ClosestPoint(collider, BodyPose.Identity, CollisionDimension.XY,
                new float3(0f, 0.5f, 0f), out float3 closest, out float3 normal);

            Assert.That(d, Is.EqualTo(-0.5f).Within(Tol));
            AssertVec3(closest, new float3(0f, 1f, 0f), "closest");
            AssertVec3(normal, new float3(0f, 1f, 0f), "normal");
        }

        // ---- Box2D ----

        [Test]
        public void Box2D_OutsidePoint_ReturnsDistanceToEdge()
        {
            var collider = Collider.Box2D(halfExtents: new float2(2f, 1f));
            float d = ShapeQuery.ClosestPoint(collider, BodyPose.Identity, CollisionDimension.XY,
                new float3(5f, 0f, 0f), out float3 closest, out float3 normal);

            Assert.That(d, Is.EqualTo(3f).Within(Tol));
            AssertVec3(closest, new float3(2f, 0f, 0f), "closest");
            AssertVec3(normal, new float3(1f, 0f, 0f), "normal");
        }

        [Test]
        public void Box2D_InsidePoint_PushesOutShallowestAxis()
        {
            var collider = Collider.Box2D(halfExtents: new float2(2f, 1f));
            float d = ShapeQuery.ClosestPoint(collider, BodyPose.Identity, CollisionDimension.XY,
                float3.zero, out float3 closest, out float3 normal);

            Assert.That(d, Is.EqualTo(-1f).Within(Tol));
            AssertVec3(closest, new float3(0f, 1f, 0f), "closest");
            AssertVec3(normal, new float3(0f, 1f, 0f), "normal");
        }

        [Test]
        public void Box2D_RotatedPose_RespectsRotation()
        {
            // 绕 Z 转 90°：局部 +X → 世界 +Y。
            var pose = new BodyPose
            {
                Position = float3.zero,
                Rotation = quaternion.AxisAngle(new float3(0f, 0f, 1f), math.PI * 0.5f),
                Scale = 1f,
            };
            var collider = Collider.Box2D(halfExtents: new float2(2f, 1f));
            float d = ShapeQuery.ClosestPoint(collider, pose, CollisionDimension.XY,
                new float3(0f, 4f, 0f), out float3 closest, out float3 normal);

            Assert.That(d, Is.EqualTo(2f).Within(Tol));
            AssertVec3(closest, new float3(0f, 2f, 0f), "closest (local +X face)");
            AssertVec3(normal, new float3(0f, 1f, 0f), "normal");
        }

        // ---- Capsule2D ----

        [Test]
        public void Capsule2D_OutsidePoint()
        {
            var collider = Collider.Capsule2D(radius: 0.5f, halfHeight: 1f, axis: 1);
            float d = ShapeQuery.ClosestPoint(collider, BodyPose.Identity, CollisionDimension.XY,
                new float3(0f, 4f, 0f), out float3 closest, out float3 normal);

            Assert.That(d, Is.EqualTo(2.5f).Within(Tol));
            AssertVec3(closest, new float3(0f, 1.5f, 0f), "closest");
            AssertVec3(normal, new float3(0f, 1f, 0f), "normal");
        }

        [Test]
        public void Capsule2D_XZDimension_MatchesXYSemantics()
        {
            var collider = Collider.Capsule2D(radius: 0.5f, halfHeight: 1f, axis: 1);
            float d = ShapeQuery.ClosestPoint(collider, BodyPose.Identity, CollisionDimension.XZ,
                new float3(0f, 9f, 4f), out float3 closest, out float3 normal);

            Assert.That(d, Is.EqualTo(2.5f).Within(Tol));
            AssertVec3(closest, new float3(0f, 9f, 1.5f), "closest (XZ plane, lifted at query Y)");
            AssertVec3(normal, new float3(0f, 0f, 1f), "normal");
        }

        // ---- Polygon2D ----

        [Test]
        public unsafe void Polygon2D_OutsidePoint_ReturnsDistanceToEdge()
        {
            var pool = stackalloc float3[4]
            {
                new float3(1f, 1f, 0f), new float3(-1f, 1f, 0f),
                new float3(-1f, -1f, 0f), new float3(1f, -1f, 0f),
            };
            var collider = Collider.Polygon2D(vertexStart: 0, vertexCount: 4);
            float d = ShapeQuery.ClosestPoint(collider, BodyPose.Identity, CollisionDimension.XY,
                new float3(3f, 0f, 0f), pool, 4, out float3 closest, out float3 normal);

            Assert.That(d, Is.EqualTo(2f).Within(Tol));
            AssertVec3(closest, new float3(1f, 0f, 0f), "closest");
            AssertVec3(normal, new float3(1f, 0f, 0f), "normal");
        }

        [Test]
        public unsafe void Polygon2D_InsidePoint_ReturnsNegativeDistanceToNearestEdge()
        {
            var pool = stackalloc float3[4]
            {
                new float3(1f, 1f, 0f), new float3(-1f, 1f, 0f),
                new float3(-1f, -1f, 0f), new float3(1f, -1f, 0f),
            };
            var collider = Collider.Polygon2D(vertexStart: 0, vertexCount: 4);
            // 顶点顺序下首条边是 y=+1：中心等距时取首条最优边。
            float d = ShapeQuery.ClosestPoint(collider, BodyPose.Identity, CollisionDimension.XY,
                float3.zero, pool, 4, out float3 closest, out float3 normal);

            Assert.That(d, Is.EqualTo(-1f).Within(Tol));
            AssertVec3(closest, new float3(0f, 1f, 0f), "closest");
            AssertVec3(normal, new float3(0f, -1f, 0f), "normal toward query");
        }

        [Test]
        public unsafe void Polygon2D_NearCorner_ReturnsDistanceToCorner()
        {
            var pool = stackalloc float3[4]
            {
                new float3(1f, 1f, 0f), new float3(-1f, 1f, 0f),
                new float3(-1f, -1f, 0f), new float3(1f, -1f, 0f),
            };
            var collider = Collider.Polygon2D(vertexStart: 0, vertexCount: 4);
            float d = ShapeQuery.ClosestPoint(collider, BodyPose.Identity, CollisionDimension.XY,
                new float3(4f, 1f, 0f), pool, 4, out float3 closest, out float3 normal);

            Assert.That(d, Is.EqualTo(3f).Within(Tol));
            AssertVec3(closest, new float3(1f, 1f, 0f), "closest is the corner vertex");
            AssertVec3(normal, new float3(1f, 0f, 0f), "normal");
        }

        [Test]
        public unsafe void Polygon2D_TransformedPose_RespectsTransform()
        {
            var pool = stackalloc float3[4]
            {
                new float3(1f, 1f, 0f), new float3(-1f, 1f, 0f),
                new float3(-1f, -1f, 0f), new float3(1f, -1f, 0f),
            };
            var pose = new BodyPose
            {
                Position = new float3(10f, 0f, 0f),
                Rotation = quaternion.identity,
                Scale = 2f,
            };
            var collider = Collider.Polygon2D(vertexStart: 0, vertexCount: 4);
            float d = ShapeQuery.ClosestPoint(collider, pose, CollisionDimension.XY,
                new float3(10f, 5f, 0f), pool, 4, out float3 closest, out float3 normal);

            Assert.That(d, Is.EqualTo(3f).Within(Tol));
            AssertVec3(closest, new float3(10f, 2f, 0f), "closest (scaled top edge)");
            AssertVec3(normal, new float3(0f, 1f, 0f), "normal");
        }

        [Test]
        public unsafe void Polygon2D_DegenerateVertexCount_FallsBackToCenterPoint()
        {
            var pool = stackalloc float3[2] { new float3(1f, 0f, 0f), new float3(-1f, 0f, 0f) };
            var collider = Collider.Polygon2D(vertexStart: 0, vertexCount: 2);
            float d = ShapeQuery.ClosestPoint(collider, BodyPose.Identity, CollisionDimension.XY,
                new float3(4f, 0f, 0f), pool, 2, out float3 closest, out _);

            Assert.That(d, Is.EqualTo(4f).Within(Tol));
            AssertVec3(closest, float3.zero, "degenerate polygon falls back to center");
        }

        // ---- 通用语义 ----

        [Test]
        public void InvalidShapeType_ReturnsZeroWithPointFallback()
        {
            var collider = new Collider((ShapeType)255, default);
            float d = ShapeQuery.ClosestPoint(collider, BodyPose.Identity, CollisionDimension.XYZ,
                new float3(1f, 2f, 3f), out float3 closest, out _);

            Assert.That(d, Is.EqualTo(0f));
            AssertVec3(closest, new float3(1f, 2f, 3f), "closest falls back to query point");
        }

        [Test]
        public void Polygon2D_WithoutPoolOverload_Throws()
        {
            var collider = Collider.Polygon2D(vertexStart: 0, vertexCount: 4);
            Assert.Throws<System.ArgumentException>(() =>
                ShapeQuery.ClosestPoint(collider, BodyPose.Identity, CollisionDimension.XY,
                    float3.zero, out _, out _));
        }
    }
}
