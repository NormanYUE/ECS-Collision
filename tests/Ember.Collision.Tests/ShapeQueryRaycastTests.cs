using Ember.Collision;
using NUnit.Framework;
using Unity.Mathematics;

namespace Ember.Collision.Tests
{
    /// <summary>
    /// ShapeQuery.Raycast（N3）解析解对拍：7 种形状各覆盖命中 / 缺失，
    /// 外加内起点、相切、零方向、maxDistance 边界与 XY/XZ 维度一致性。
    /// </summary>
    [TestFixture]
    public class ShapeQueryRaycastTests
    {
        private const float Tol = 1e-4f;

        private static void AssertVec3(float3 actual, float3 expected, string what)
        {
            Assert.That(math.distance(actual, expected), Is.LessThan(Tol), $"{what}: got {actual}, want {expected}");
        }

        // ---- 边界：方向与距离 ----

        [Test]
        public void ZeroDirection_MissesAndZeroesOutputs()
        {
            var collider = Collider.Sphere(radius: 1f);
            bool hit = ShapeQuery.Raycast(collider, BodyPose.Identity, new float3(5f, 0f, 0f),
                float3.zero, 100f, out float distance, out float3 point, out float3 normal);

            Assert.That(hit, Is.False);
            Assert.That(distance, Is.EqualTo(0f));
            AssertVec3(point, new float3(5f, 0f, 0f), "point falls back to origin");
            AssertVec3(normal, float3.zero, "normal");
        }

        [Test]
        public void NegativeMaxDistance_Misses()
        {
            var collider = Collider.Sphere(radius: 1f);
            bool hit = ShapeQuery.Raycast(collider, BodyPose.Identity, new float3(5f, 0f, 0f),
                new float3(-1f, 0f, 0f), -1f, out _, out _, out _);

            Assert.That(hit, Is.False);
        }

        [Test]
        public void ZeroMaxDistance_MissesWhenOriginOutside()
        {
            var collider = Collider.Sphere(radius: 1f);
            bool hit = ShapeQuery.Raycast(collider, BodyPose.Identity, new float3(5f, 0f, 0f),
                new float3(-1f, 0f, 0f), 0f, out _, out _, out _);

            Assert.That(hit, Is.False);
        }

        [Test]
        public void ZeroMaxDistance_HitsWhenOriginInside()
        {
            var collider = Collider.Sphere(radius: 2f, center: new float3(0f, 0f, 1f));
            // 起点 (0,0,0) 距球心 1 < 半径 2：在形状内。
            bool hit = ShapeQuery.Raycast(collider, BodyPose.Identity, float3.zero,
                new float3(0f, 0f, -1f), 0f, out float distance, out float3 point, out float3 normal);

            Assert.That(hit, Is.True);
            Assert.That(distance, Is.EqualTo(0f));
            AssertVec3(point, float3.zero, "point");
            AssertVec3(normal, new float3(0f, 0f, -1f), "inside normal points from surface toward origin");
        }

        [Test]
        public void NoneShape_Misses()
        {
            bool hit = ShapeQuery.Raycast(default(Collider), BodyPose.Identity, new float3(5f, 0f, 0f),
                new float3(-1f, 0f, 0f), 100f, out _, out _, out _);

            Assert.That(hit, Is.False);
        }

        // ---- Sphere ----

        [Test]
        public void Sphere_RayThroughCenter_HitsNearSurface()
        {
            var collider = Collider.Sphere(radius: 2f, center: new float3(1f, 2f, 3f));
            bool hit = ShapeQuery.Raycast(collider, BodyPose.Identity, new float3(10f, 2f, 3f),
                new float3(-1f, 0f, 0f), 100f, out float distance, out float3 point, out float3 normal);

            Assert.That(hit, Is.True);
            Assert.That(distance, Is.EqualTo(7f).Within(Tol));
            AssertVec3(point, new float3(3f, 2f, 3f), "point");
            AssertVec3(normal, new float3(1f, 0f, 0f), "normal points back at the ray");
        }

        [Test]
        public void Sphere_UnnormalizedDirection_ReportsWorldDistance()
        {
            var collider = Collider.Sphere(radius: 2f, center: new float3(1f, 2f, 3f));
            bool hit = ShapeQuery.Raycast(collider, BodyPose.Identity, new float3(10f, 2f, 3f),
                new float3(-3f, 0f, 0f), 100f, out float distance, out float3 point, out _);

            Assert.That(hit, Is.True);
            Assert.That(distance, Is.EqualTo(7f).Within(Tol));
            AssertVec3(point, new float3(3f, 2f, 3f), "point");
        }

        [Test]
        public void Sphere_MaxDistanceTooShort_Misses()
        {
            var collider = Collider.Sphere(radius: 2f, center: new float3(1f, 2f, 3f));
            bool hit = ShapeQuery.Raycast(collider, BodyPose.Identity, new float3(10f, 2f, 3f),
                new float3(-1f, 0f, 0f), 6.9f, out _, out _, out _);

            Assert.That(hit, Is.False);
        }

        [Test]
        public void Sphere_Tangent_CountsAsHit()
        {
            var collider = Collider.Sphere(radius: 2f, center: new float3(1f, 2f, 3f));
            // 擦过上表面 y = 4
            bool hit = ShapeQuery.Raycast(collider, BodyPose.Identity, new float3(10f, 4f, 3f),
                new float3(-1f, 0f, 0f), 100f, out float distance, out float3 point, out float3 normal);

            Assert.That(hit, Is.True);
            Assert.That(distance, Is.EqualTo(9f).Within(Tol));
            AssertVec3(point, new float3(1f, 4f, 3f), "point");
            AssertVec3(normal, new float3(0f, 1f, 0f), "normal");
        }

        [Test]
        public void Sphere_OriginOnSurface_HitsAtZero()
        {
            var collider = Collider.Sphere(radius: 2f, center: new float3(1f, 2f, 3f));
            bool hit = ShapeQuery.Raycast(collider, BodyPose.Identity, new float3(3f, 2f, 3f),
                new float3(1f, 0f, 0f), 100f, out float distance, out float3 point, out float3 normal);

            Assert.That(hit, Is.True);
            Assert.That(distance, Is.EqualTo(0f));
            AssertVec3(point, new float3(3f, 2f, 3f), "point");
            AssertVec3(normal, new float3(1f, 0f, 0f), "normal");
        }

        // ---- Capsule (3D) ----

        [Test]
        public void Capsule_RayHitsCylinderSide()
        {
            var collider = Collider.Capsule(radius: 1f, halfHeight: 2f);
            bool hit = ShapeQuery.Raycast(collider, BodyPose.Identity, new float3(5f, 0f, 0f),
                new float3(-1f, 0f, 0f), 100f, out float distance, out float3 point, out float3 normal);

            Assert.That(hit, Is.True);
            Assert.That(distance, Is.EqualTo(4f).Within(Tol));
            AssertVec3(point, new float3(1f, 0f, 0f), "point");
            AssertVec3(normal, new float3(1f, 0f, 0f), "normal");
        }

        [Test]
        public void Capsule_RayHitsEndCap()
        {
            var collider = Collider.Capsule(radius: 1f, halfHeight: 2f);
            bool hit = ShapeQuery.Raycast(collider, BodyPose.Identity, new float3(0f, 10f, 0f),
                new float3(0f, -1f, 0f), 100f, out float distance, out float3 point, out float3 normal);

            Assert.That(hit, Is.True);
            Assert.That(distance, Is.EqualTo(7f).Within(Tol));
            AssertVec3(point, new float3(0f, 3f, 0f), "point");
            AssertVec3(normal, new float3(0f, 1f, 0f), "normal");
        }

        [Test]
        public void Capsule_ParallelToAxis_HitsCap()
        {
            var collider = Collider.Capsule(radius: 1f, halfHeight: 2f);
            // 平行于轴：柱面二次方程退化为 a = 0，必须由端帽给出正解
            bool hit = ShapeQuery.Raycast(collider, BodyPose.Identity, new float3(0.5f, 10f, 0f),
                new float3(0f, -1f, 0f), 100f, out float distance, out float3 point, out _);

            Assert.That(hit, Is.True);
            Assert.That(distance, Is.EqualTo(10f - 2f - math.sqrt(0.75f)).Within(Tol));
            Assert.That(point.y, Is.EqualTo(2f + math.sqrt(0.75f)).Within(Tol));
            Assert.That(point.x, Is.EqualTo(0.5f).Within(Tol));
        }

        [Test]
        public void Capsule_RayMissesBeyondCap()
        {
            var collider = Collider.Capsule(radius: 1f, halfHeight: 2f);
            // 柱面二次方程有解，但轴向投影落在轴段外；端帽也不相交
            bool hit = ShapeQuery.Raycast(collider, BodyPose.Identity, new float3(5f, 3.5f, 0f),
                new float3(-1f, 0f, 0f), 100f, out _, out _, out _);

            Assert.That(hit, Is.False);
        }

        [Test]
        public void Capsule_OriginInside_HitsAtZero()
        {
            var collider = Collider.Capsule(radius: 1f, halfHeight: 2f);
            bool hit = ShapeQuery.Raycast(collider, BodyPose.Identity, new float3(0.5f, 0f, 0f),
                new float3(1f, 0f, 0f), 100f, out float distance, out _, out float3 normal);

            Assert.That(hit, Is.True);
            Assert.That(distance, Is.EqualTo(0f));
            AssertVec3(normal, new float3(1f, 0f, 0f), "normal");
        }

        // ---- Box (3D) ----

        [Test]
        public void Box_RayHitsFace()
        {
            var collider = Collider.Box(new float3(1f, 2f, 3f));
            bool hit = ShapeQuery.Raycast(collider, BodyPose.Identity, new float3(10f, 0f, 0f),
                new float3(-1f, 0f, 0f), 100f, out float distance, out float3 point, out float3 normal);

            Assert.That(hit, Is.True);
            Assert.That(distance, Is.EqualTo(9f).Within(Tol));
            AssertVec3(point, new float3(1f, 0f, 0f), "point");
            AssertVec3(normal, new float3(1f, 0f, 0f), "normal");
        }

        [Test]
        public void Box_RayHitsNearerFace()
        {
            var collider = Collider.Box(new float3(1f, 2f, 3f));
            float3 direction = math.normalize(new float3(-1f, -1f, 0f));
            // 斜射：+X 面与 +Y 面的入射参数取较大者，+X 面更晚，故命中 +X 面
            bool hit = ShapeQuery.Raycast(collider, BodyPose.Identity, new float3(5f, 5f, 0f),
                direction, 100f, out float distance, out float3 point, out float3 normal);

            Assert.That(hit, Is.True);
            Assert.That(distance, Is.EqualTo(4f * math.sqrt(2f)).Within(Tol));
            AssertVec3(point, new float3(1f, 1f, 0f), "point");
            AssertVec3(normal, new float3(1f, 0f, 0f), "normal");
        }

        [Test]
        public void Box_RayMisses()
        {
            var collider = Collider.Box(new float3(1f, 2f, 3f));
            bool hit = ShapeQuery.Raycast(collider, BodyPose.Identity, new float3(10f, 5f, 0f),
                new float3(-1f, 0f, 0f), 100f, out _, out _, out _);

            Assert.That(hit, Is.False);
        }

        [Test]
        public void Box_Rotated_UsesLocalAxes()
        {
            var collider = Collider.Box(new float3(1f, 2f, 3f));
            // 绕 Z 转 90°：局部 X 轴映射到世界 Y，故世界范围变为 x ∈ ±2
            var pose = new BodyPose
            {
                Position = float3.zero,
                Rotation = quaternion.AxisAngle(new float3(0f, 0f, 1f), math.PI * 0.5f),
                Scale = 1f,
            };

            bool hit = ShapeQuery.Raycast(collider, pose, new float3(10f, 0f, 0f),
                new float3(-1f, 0f, 0f), 100f, out float distance, out float3 point, out float3 normal);

            Assert.That(hit, Is.True);
            Assert.That(distance, Is.EqualTo(8f).Within(Tol));
            AssertVec3(point, new float3(2f, 0f, 0f), "point");
            AssertVec3(normal, new float3(1f, 0f, 0f), "world-space normal");
        }

        [Test]
        public void Box_OriginInside_HitsAtZero()
        {
            var collider = Collider.Box(new float3(1f, 2f, 3f));
            bool hit = ShapeQuery.Raycast(collider, BodyPose.Identity, float3.zero,
                new float3(1f, 0f, 0f), 100f, out float distance, out _, out _);

            Assert.That(hit, Is.True);
            Assert.That(distance, Is.EqualTo(0f));
        }

        // ---- Circle (2D) ----

        [Test]
        public void Circle_XyPlane_Hits()
        {
            var collider = Collider.Circle(radius: 2f);
            bool hit = ShapeQuery.Raycast(collider, BodyPose.Identity, CollisionDimension.XY,
                new float3(5f, 0f, 0f), new float3(-1f, 0f, 0f), 100f,
                out float distance, out float3 point, out float3 normal);

            Assert.That(hit, Is.True);
            Assert.That(distance, Is.EqualTo(3f).Within(Tol));
            AssertVec3(point, new float3(2f, 0f, 0f), "point");
            AssertVec3(normal, new float3(1f, 0f, 0f), "normal");
        }

        [Test]
        public void Circle_XzPlane_Hits()
        {
            var collider = Collider.Circle(radius: 2f);
            bool hit = ShapeQuery.Raycast(collider, BodyPose.Identity, CollisionDimension.XZ,
                new float3(0f, 0f, 5f), new float3(0f, 0f, -1f), 100f,
                out float distance, out float3 point, out float3 normal);

            Assert.That(hit, Is.True);
            Assert.That(distance, Is.EqualTo(3f).Within(Tol));
            AssertVec3(point, new float3(0f, 0f, 2f), "point");
            AssertVec3(normal, new float3(0f, 0f, 1f), "normal");
        }

        [Test]
        public void Circle_RayPerpendicularToPlane_FromOutsidePrism_Misses()
        {
            var collider = Collider.Circle(radius: 2f);
            // 投影落在圆外：2D 形状是沿无效轴无限延伸的棱柱，垂直于平面时无有限交点
            bool hit = ShapeQuery.Raycast(collider, BodyPose.Identity, CollisionDimension.XY,
                new float3(5f, 0f, -5f), new float3(0f, 0f, 1f), 100f, out _, out _, out _);

            Assert.That(hit, Is.False);
        }

        [Test]
        public void Circle_RayPerpendicularToPlane_FromInsidePrism_HitsAtZero()
        {
            var collider = Collider.Circle(radius: 2f);
            // 投影落在圆内：起点在棱柱内，立即命中
            bool hit = ShapeQuery.Raycast(collider, BodyPose.Identity, CollisionDimension.XY,
                new float3(0f, 0f, -5f), new float3(0f, 0f, 1f), 100f,
                out float distance, out float3 point, out float3 normal);

            Assert.That(hit, Is.True);
            Assert.That(distance, Is.EqualTo(0f));
            AssertVec3(point, new float3(0f, 0f, -5f), "point falls back to origin");
            AssertVec3(normal, new float3(1f, 0f, 0f), "degenerate inside normal falls back");
        }

        [Test]
        public void Circle_ObliqueRay_ReportsWorldDistanceAndPoint()
        {
            var collider = Collider.Circle(radius: 2f);
            float3 direction = math.normalize(new float3(-1f, 0f, -1f));
            // XY 平面内的截线沿 -X 走到 x = 2；世界参数按 |project(direction)| 放大
            bool hit = ShapeQuery.Raycast(collider, BodyPose.Identity, CollisionDimension.XY,
                new float3(5f, 0f, 0f), direction, 100f,
                out float distance, out float3 point, out float3 normal);

            Assert.That(hit, Is.True);
            Assert.That(distance, Is.EqualTo(3f * math.sqrt(2f)).Within(Tol));
            AssertVec3(point, new float3(2f, 0f, -3f), "point");
            AssertVec3(normal, new float3(1f, 0f, 0f), "normal");
        }

        // ---- Box2D ----

        [Test]
        public void Box2D_XyPlane_Hits()
        {
            var collider = Collider.Box2D(new float2(1f, 2f));
            bool hit = ShapeQuery.Raycast(collider, BodyPose.Identity, CollisionDimension.XY,
                new float3(5f, 0f, 0f), new float3(-1f, 0f, 0f), 100f,
                out float distance, out float3 point, out float3 normal);

            Assert.That(hit, Is.True);
            Assert.That(distance, Is.EqualTo(4f).Within(Tol));
            AssertVec3(point, new float3(1f, 0f, 0f), "point");
            AssertVec3(normal, new float3(1f, 0f, 0f), "normal");
        }

        [Test]
        public void Box2D_XzPlane_MapsVExtentOntoZ()
        {
            var collider = Collider.Box2D(new float2(1f, 2f));
            bool hit = ShapeQuery.Raycast(collider, BodyPose.Identity, CollisionDimension.XZ,
                new float3(0f, 0f, 5f), new float3(0f, 0f, -1f), 100f,
                out float distance, out float3 point, out float3 normal);

            Assert.That(hit, Is.True);
            Assert.That(distance, Is.EqualTo(3f).Within(Tol));
            AssertVec3(point, new float3(0f, 0f, 2f), "point");
            AssertVec3(normal, new float3(0f, 0f, 1f), "normal");
        }

        [Test]
        public void Box2D_RayOutsideVExtent_Misses()
        {
            var collider = Collider.Box2D(new float2(1f, 2f));
            bool hit = ShapeQuery.Raycast(collider, BodyPose.Identity, CollisionDimension.XY,
                new float3(5f, 3f, 0f), new float3(-1f, 0f, 0f), 100f, out _, out _, out _);

            Assert.That(hit, Is.False);
        }

        // ---- Capsule2D ----

        [Test]
        public void Capsule2D_XyPlane_HitsSide()
        {
            var collider = Collider.Capsule2D(radius: 1f, halfHeight: 2f);
            bool hit = ShapeQuery.Raycast(collider, BodyPose.Identity, CollisionDimension.XY,
                new float3(5f, 0f, 0f), new float3(-1f, 0f, 0f), 100f,
                out float distance, out float3 point, out float3 normal);

            Assert.That(hit, Is.True);
            Assert.That(distance, Is.EqualTo(4f).Within(Tol));
            AssertVec3(point, new float3(1f, 0f, 0f), "point");
            AssertVec3(normal, new float3(1f, 0f, 0f), "normal");
        }

        [Test]
        public void Capsule2D_XyPlane_HitsCap()
        {
            var collider = Collider.Capsule2D(radius: 1f, halfHeight: 2f);
            bool hit = ShapeQuery.Raycast(collider, BodyPose.Identity, CollisionDimension.XY,
                new float3(0f, 10f, 0f), new float3(0f, -1f, 0f), 100f,
                out float distance, out float3 point, out float3 normal);

            Assert.That(hit, Is.True);
            Assert.That(distance, Is.EqualTo(7f).Within(Tol));
            AssertVec3(point, new float3(0f, 3f, 0f), "point");
            AssertVec3(normal, new float3(0f, 1f, 0f), "normal");
        }

        // ---- Polygon2D ----

        [Test]
        public unsafe void Polygon2D_RayHitsEdge()
        {
            var pool = stackalloc float3[4]
            {
                new float3(1f, 1f, 0f), new float3(-1f, 1f, 0f),
                new float3(-1f, -1f, 0f), new float3(1f, -1f, 0f),
            };
            var collider = Collider.Polygon2D(vertexStart: 0, vertexCount: 4);
            bool hit = ShapeQuery.Raycast(collider, BodyPose.Identity, CollisionDimension.XY,
                new float3(5f, 0f, 0f), new float3(-1f, 0f, 0f), 100f, pool, 4,
                out float distance, out float3 point, out float3 normal);

            Assert.That(hit, Is.True);
            Assert.That(distance, Is.EqualTo(4f).Within(Tol));
            AssertVec3(point, new float3(1f, 0f, 0f), "point");
            AssertVec3(normal, new float3(1f, 0f, 0f), "normal faces the ray");
        }

        [Test]
        public unsafe void Polygon2D_RayMisses()
        {
            var pool = stackalloc float3[4]
            {
                new float3(1f, 1f, 0f), new float3(-1f, 1f, 0f),
                new float3(-1f, -1f, 0f), new float3(1f, -1f, 0f),
            };
            var collider = Collider.Polygon2D(vertexStart: 0, vertexCount: 4);
            bool hit = ShapeQuery.Raycast(collider, BodyPose.Identity, CollisionDimension.XY,
                new float3(5f, 5f, 0f), new float3(-1f, 0f, 0f), 100f, pool, 4,
                out _, out _, out _);

            Assert.That(hit, Is.False);
        }

        [Test]
        public unsafe void Polygon2D_VertexOffsetPool_UsesOnlyItsOwnVertices()
        {
            // 池前面塞入干扰顶点：必须只读 [VertexStart, VertexStart + VertexCount)
            var pool = stackalloc float3[6]
            {
                new float3(99f, 99f, 0f), new float3(-99f, 99f, 0f),
                new float3(1f, 1f, 0f), new float3(-1f, 1f, 0f),
                new float3(-1f, -1f, 0f), new float3(1f, -1f, 0f),
            };
            var collider = Collider.Polygon2D(vertexStart: 2, vertexCount: 4);
            bool hit = ShapeQuery.Raycast(collider, BodyPose.Identity, CollisionDimension.XY,
                new float3(5f, 0f, 0f), new float3(-1f, 0f, 0f), 100f, pool, 6,
                out float distance, out float3 point, out _);

            Assert.That(hit, Is.True);
            Assert.That(distance, Is.EqualTo(4f).Within(Tol));
            AssertVec3(point, new float3(1f, 0f, 0f), "point");
        }

        [Test]
        public void Polygon2D_WithoutVertexPool_Throws()
        {
            var collider = Collider.Polygon2D(vertexStart: 0, vertexCount: 4);
            Assert.Throws<System.ArgumentException>(() =>
                ShapeQuery.Raycast(collider, BodyPose.Identity, CollisionDimension.XY,
                    new float3(5f, 0f, 0f), new float3(-1f, 0f, 0f), 100f, out _, out _, out _));
        }

        // ---- 3D 便捷重载 ----

        [Test]
        public void ThreeDimensionalOverload_MatchesExplicitXyz()
        {
            var collider = Collider.Sphere(radius: 2f, center: new float3(1f, 2f, 3f));
            bool hit = ShapeQuery.Raycast(collider, BodyPose.Identity, new float3(10f, 2f, 3f),
                new float3(-1f, 0f, 0f), 100f, out float distance, out float3 point, out _);

            Assert.That(hit, Is.True);
            Assert.That(distance, Is.EqualTo(7f).Within(Tol));
            AssertVec3(point, new float3(3f, 2f, 3f), "point");
        }
    }
}
