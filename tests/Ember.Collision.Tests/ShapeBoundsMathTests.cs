using NUnit.Framework;
using Unity.Mathematics;

namespace Ember.Collision.Tests
{
    /// <summary>
    /// 形状本地包围盒与世界包围盒换算测试。
    /// 世界包围盒必须<b>不漏包</b>（否则宽相会漏 pair），这是本组用例的核心断言。
    /// </summary>
    [TestFixture]
    public class ShapeBoundsMathTests
    {
        [Test]
        unsafe public void Sphere_LocalBounds_IsRadiusCube()
        {
            var collider = Collider.Sphere(2f);
            ShapeBoundsMath.LocalBounds(collider, CollisionDimension.XYZ, null, out float3 center, out float3 extents);

            Assert.That(center, Is.EqualTo(float3.zero));
            Assert.That(extents, Is.EqualTo(new float3(2f)));
        }

        [Test]
        unsafe public void Sphere_LocalBounds_NegativeRadius_UsesAbsoluteRadius()
        {
            ShapeBoundsMath.LocalBounds(Collider.Sphere(-2f), CollisionDimension.XYZ, null, out _, out float3 extents);

            Assert.That(extents, Is.EqualTo(new float3(2f)));
        }

        [Test]
        unsafe public void Box_LocalBounds_IsAbsHalfExtents()
        {
            var collider = Collider.Box(new float3(-1f, 2f, -3f));
            ShapeBoundsMath.LocalBounds(collider, CollisionDimension.XYZ, null, out _, out float3 extents);

            Assert.That(extents, Is.EqualTo(new float3(1f, 2f, 3f)));
        }

        [TestCase((byte)0)]
        [TestCase((byte)1)]
        [TestCase((byte)2)]
        unsafe public void Capsule_LocalBounds_PutsLongAxisOnCapsuleAxis(byte axis)
        {
            ShapeBoundsMath.LocalBounds(
                Collider.Capsule(1f, 3f, axis), CollisionDimension.XYZ, null, out _, out float3 extents);

            float3 expected = ShapeBoundsMath.CapsuleExtents(1f, 3f, axis);
            Assert.That(extents, Is.EqualTo(expected));
            Assert.That(expected[axis], Is.EqualTo(4f), "沿胶囊轴的半范围应为 halfHeight + radius");
        }

        [Test]
        unsafe public void Capsule_LocalBounds_NegativeDimensions_UsesAbsoluteSize()
        {
            ShapeBoundsMath.LocalBounds(
                Collider.Capsule(-1f, -3f, 1), CollisionDimension.XYZ, null, out _, out float3 extents);

            Assert.That(extents, Is.EqualTo(new float3(1f, 4f, 1f)));
        }

        [Test]
        unsafe public void TwoDimensionalMode_ZeroesInactiveAxis()
        {
            ShapeBoundsMath.LocalBounds(Collider.Circle(1.5f), CollisionDimension.XY, null, out _, out float3 xy);
            Assert.That(xy, Is.EqualTo(new float3(1.5f, 1.5f, 0f)));

            ShapeBoundsMath.LocalBounds(Collider.Circle(1.5f), CollisionDimension.XZ, null, out _, out float3 xz);
            Assert.That(xz, Is.EqualTo(new float3(1.5f, 0f, 1.5f)));
        }

        [Test]
        unsafe public void Box2D_LocalBounds_XZ_MapsPackedVExtentToZ()
        {
            ShapeBoundsMath.LocalBounds(
                Collider.Box2D(new float2(2f, 3f)), CollisionDimension.XZ, null,
                out _, out float3 extents);

            Assert.That(extents, Is.EqualTo(new float3(2f, 0f, 3f)));
        }

        [Test]
        unsafe public void CenterOffset_ShiftsLocalBounds()
        {
            var collider = Collider.Sphere(1f, new float3(5f, 0f, 0f));
            ShapeBoundsMath.LocalBounds(collider, CollisionDimension.XYZ, null, out float3 center, out _);

            Assert.That(center, Is.EqualTo(new float3(5f, 0f, 0f)));
        }

        [Test]
        public void ToWorldBounds_IdentityMatrix_IsUnchanged()
        {
            ShapeBoundsMath.ToWorldBounds(
                float4x4.identity, new float3(1f, 2f, 3f), new float3(1f), out float3 center, out float3 extents);

            Assert.That(center, Is.EqualTo(new float3(1f, 2f, 3f)));
            Assert.That(extents, Is.EqualTo(new float3(1f)));
        }

        [Test]
        public void ToWorldBounds_Translation_MovesCenterOnly()
        {
            var m = float4x4.Translate(new float3(10f, 20f, 30f));
            ShapeBoundsMath.ToWorldBounds(m, float3.zero, new float3(2f), out float3 center, out float3 extents);

            Assert.That(center, Is.EqualTo(new float3(10f, 20f, 30f)));
            Assert.That(extents, Is.EqualTo(new float3(2f)));
        }

        [Test]
        public void ToWorldBounds_UniformScale_ScalesExtents()
        {
            var m = float4x4.Scale(new float3(3f));
            ShapeBoundsMath.ToWorldBounds(m, float3.zero, new float3(1f), out _, out float3 extents);

            Assert.That(extents, Is.EqualTo(new float3(3f)));
        }

        [Test]
        public void ToWorldBounds_45DegreeRotation_IsConServativeNotMinimal()
        {
            // 边长 2 的立方体绕 Z 轴 45°：紧致 AABB 半范围应为 (√2, √2, 1)。
            var m = float4x4.RotateZ(math.radians(45f));
            ShapeBoundsMath.ToWorldBounds(m, float3.zero, new float3(1f), out _, out float3 extents);

            float sqrt2 = math.sqrt(2f);
            Assert.That(extents.x, Is.EqualTo(sqrt2).Within(1e-4f), "旋转必须在 X 轴上产生 √2 的包围扩展");
            Assert.That(extents.y, Is.EqualTo(sqrt2).Within(1e-4f));
            Assert.That(extents.z, Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void ToWorldBounds_NeverUnderCoversRotatedBoxCorners()
        {
            // 逐一验证 8 个角点都落在算出的世界 AABB 内——这是「不漏包」的穷举证明。
            var rotation = math.mul(
                float4x4.RotateX(math.radians(37f)),
                math.mul(float4x4.RotateY(math.radians(61f)), float4x4.RotateZ(math.radians(23f))));
            var m = math.mul(float4x4.Translate(new float3(4f, -2f, 7f)), math.mul(rotation, float4x4.Scale(new float3(1.5f, 2.5f, 0.5f))));

            float3 localExtents = new(1f, 1f, 1f);
            ShapeBoundsMath.ToWorldBounds(m, float3.zero, localExtents, out float3 center, out float3 extents);
            var worldBox = Aabb.FromCenterExtents(center, extents);

            for (int i = 0; i < 8; i++)
            {
                float3 corner = new(
                    (i & 1) == 0 ? -localExtents.x : localExtents.x,
                    (i & 2) == 0 ? -localExtents.y : localExtents.y,
                    (i & 4) == 0 ? -localExtents.z : localExtents.z);
                float3 world = math.transform(m, corner);

                Assert.That(worldBox.Contains(world), Is.True,
                    $"角点 {i} ({world}) 落在世界包围盒 {worldBox.Min}..{worldBox.Max} 之外——宽相会漏 pair");
            }
        }

        [Test]
        public void WorldShapeExtents_TwoDimensionalMode_InflatesInactiveAxis()
        {
            ShapeBoundsMath.WorldShapeExtents(
                float4x4.identity, new float3(1f, 1f, 0f), CollisionDimension.XY, out float3 xy);

            Assert.That(xy.z, Is.EqualTo(Aabb.InactiveAxisHalfExtent));
            Assert.That(xy.x, Is.EqualTo(1f));

            ShapeBoundsMath.WorldShapeExtents(
                float4x4.identity, new float3(1f, 0f, 1f), CollisionDimension.XZ, out float3 xz);

            Assert.That(xz.y, Is.EqualTo(Aabb.InactiveAxisHalfExtent));
        }

        [Test]
        public unsafe void Convex_LocalBounds_UsesVertexRange()
        {
            float3* vertices = stackalloc float3[4];
            vertices[0] = new float3(-1f, -2f, 0f);
            vertices[1] = new float3(3f, -2f, 0f);
            vertices[2] = new float3(3f, 2f, 0f);
            vertices[3] = new float3(-1f, 2f, 0f);

            var collider = new Collider(ShapeType.Polygon2D, ShapeParams.ForConvex(0, 4));
            ShapeBoundsMath.LocalBounds(collider, CollisionDimension.XY, vertices, out _, out float3 extents);

            Assert.That(extents, Is.EqualTo(new float3(2f, 2f, 0f)));
        }

        [Test]
        public unsafe void Polygon2D_LocalBounds_OffsetsCenterByVertexRange()
        {
            float3* vertices = stackalloc float3[4];
            vertices[0] = new float3(-1f, -2f, 0f);
            vertices[1] = new float3(3f, -2f, 0f);
            vertices[2] = new float3(3f, 2f, 0f);
            vertices[3] = new float3(-1f, 2f, 0f);

            var collider = new Collider(
                ShapeType.Polygon2D,
                ShapeParams.ForConvex(0, 4, new float3(5f, 7f, 0f)));

            ShapeBoundsMath.LocalBounds(collider, CollisionDimension.XY, vertices, out float3 center, out float3 extents);

            Assert.That(center, Is.EqualTo(new float3(6f, 7f, 0f)));
            Assert.That(extents, Is.EqualTo(new float3(2f, 2f, 0f)));
        }

        [Test]
        public unsafe void Convex_ZeroVertices_DoesNotCrash()
        {
            var collider = new Collider(ShapeType.Convex, ShapeParams.ForConvex(0, 0));
            ShapeBoundsMath.LocalBounds(collider, CollisionDimension.XYZ, null, out float3 center, out float3 extents);

            Assert.That(center, Is.EqualTo(float3.zero));
            Assert.That(extents, Is.EqualTo(float3.zero));
        }
    }
}
