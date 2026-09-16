using NUnit.Framework;
using Unity.Mathematics;

namespace Ember.Collision.Tests
{
    /// <summary><see cref="Aabb"/> 纯函数测试，含 2D 无效轴撑开的语义验证。</summary>
    [TestFixture]
    public class AabbTests
    {
        [Test]
        public void FromCenterExtents_Roundtrips()
        {
            var box = Aabb.FromCenterExtents(new float3(1f, 2f, 3f), new float3(1f, 1f, 1f));

            Assert.That(box.Center, Is.EqualTo(new float3(1f, 2f, 3f)));
            Assert.That(box.Extents, Is.EqualTo(new float3(1f, 1f, 1f)));
        }

        [Test]
        public void Overlaps_TouchingFaces_IsTrue()
        {
            var a = Aabb.FromCenterExtents(float3.zero, new float3(1f));
            var b = Aabb.FromCenterExtents(new float3(2f, 0f, 0f), new float3(1f));

            // 面贴面（恰好接触）必须算重叠，否则接触约束会被丢掉。
            Assert.That(Aabb.Overlaps(a, b), Is.True);
        }

        [Test]
        public void Overlaps_SeparatedByEpsilon_IsFalse()
        {
            var a = Aabb.FromCenterExtents(float3.zero, new float3(1f));
            var b = Aabb.FromCenterExtents(new float3(2.001f, 0f, 0f), new float3(1f));

            Assert.That(Aabb.Overlaps(a, b), Is.False);
        }

        [Test]
        public void Overlaps_IsSymmetric()
        {
            var a = Aabb.FromCenterExtents(new float3(0f, 0f, 0f), new float3(2f, 1f, 3f));
            var b = Aabb.FromCenterExtents(new float3(1f, 1f, 1f), new float3(1f));

            Assert.That(Aabb.Overlaps(a, b), Is.EqualTo(Aabb.Overlaps(b, a)));
        }

        [Test]
        public void InflateInactiveAxis_XY_MakesZOverlapAlways()
        {
            var a = Aabb.FromCenterExtents(new float3(0f, 0f, 0f), new float3(1f));
            var b = Aabb.FromCenterExtents(new float3(0.5f, 0f, 500f), new float3(1f));

            // 撑开前：z 相距 500，不重叠。
            Assert.That(Aabb.Overlaps(a, b), Is.False);

            var ai = Aabb.InflateInactiveAxis(a, CollisionDimension.XY);
            var bi = Aabb.InflateInactiveAxis(b, CollisionDimension.XY);

            // 撑开后：z 轴恒重叠，2D 语义生效。
            Assert.That(Aabb.Overlaps(ai, bi), Is.True);
        }

        [Test]
        public void InflateInactiveAxis_XZ_MakesYOverlapAlways()
        {
            var a = Aabb.FromCenterExtents(new float3(0f, 0f, 0f), new float3(1f));
            var b = Aabb.FromCenterExtents(new float3(0f, -800f, 0.5f), new float3(1f));

            var ai = Aabb.InflateInactiveAxis(a, CollisionDimension.XZ);
            var bi = Aabb.InflateInactiveAxis(b, CollisionDimension.XZ);

            Assert.That(Aabb.Overlaps(ai, bi), Is.True);
        }

        [Test]
        public void InflateInactiveAxis_DoesNotChangeActiveAxes()
        {
            var box = Aabb.FromCenterExtents(new float3(3f, 4f, 5f), new float3(1f, 2f, 3f));
            var inflated = Aabb.InflateInactiveAxis(box, CollisionDimension.XY);

            Assert.That(inflated.Min.x, Is.EqualTo(box.Min.x));
            Assert.That(inflated.Max.x, Is.EqualTo(box.Max.x));
            Assert.That(inflated.Min.y, Is.EqualTo(box.Min.y));
            Assert.That(inflated.Max.y, Is.EqualTo(box.Max.y));
        }

        [Test]
        public void Union_CoversBoth()
        {
            var a = Aabb.FromCenterExtents(new float3(-5f, 0f, 0f), new float3(1f));
            var b = Aabb.FromCenterExtents(new float3(5f, 0f, 0f), new float3(1f));
            var u = Aabb.Union(a, b);

            Assert.That(Aabb.Contains(u, a), Is.True);
            Assert.That(Aabb.Contains(u, b), Is.True);
            Assert.That(u.Min.x, Is.EqualTo(-6f));
            Assert.That(u.Max.x, Is.EqualTo(6f));
        }

        [Test]
        public void Encapsulate_ExpandsToCoverPoint()
        {
            var box = Aabb.Empty;
            box.Encapsulate(new float3(1f, 2f, 3f));
            box.Encapsulate(new float3(-1f, 0f, 5f));

            Assert.That(box.Min, Is.EqualTo(new float3(-1f, 0f, 3f)));
            Assert.That(box.Max, Is.EqualTo(new float3(1f, 2f, 5f)));
        }

        [Test]
        public void OverlapsVolume_ComputesIntersectionVolume()
        {
            var a = Aabb.FromCenterExtents(float3.zero, new float3(1f));
            var b = Aabb.FromCenterExtents(new float3(1f, 0f, 0f), new float3(1f));

            Assert.That(Aabb.OverlapsVolume(a, b, out float volume), Is.True);
            Assert.That(volume, Is.EqualTo(2f * 2f * 1f).Within(1e-5f));
        }

        [Test]
        public void Empty_IsEmpty()
        {
            Assert.That(Aabb.Empty.IsEmpty, Is.True);
            Assert.That(Aabb.FromCenterExtents(float3.zero, new float3(1f)).IsEmpty, Is.False);
        }
    }
}
