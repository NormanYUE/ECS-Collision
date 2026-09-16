using NUnit.Framework;
using Unity.Mathematics;

namespace Ember.Collision.Tests
{
    /// <summary>
    /// Morton 编码器纯函数测试。重点验证位散布的<b>双射性</b>——
    /// 一旦有位重叠，宽相排序会把不同体素折叠到同一码，
    /// 表现为「偶发漏检」，是极难定位的一类 bug。
    /// </summary>
    [TestFixture]
    public class MortonCoderTests
    {
        [Test]
        public void Part1By1_IsBijective_OverFull10BitRange()
        {
            var seen = new bool[1 << 20];
            for (uint value = 0; value <= MortonCoder.AxisMax; value++)
            {
                uint spread = MortonCoder.Part1By1(value);
                Assert.That(spread, Is.LessThan(1u << 20), $"Part1By1({value}) 越界: {spread}");
                Assert.That(seen[spread], Is.False, $"Part1By1 在 {value} 与更低值之间发生位冲突: {spread}");
                seen[spread] = true;
            }
        }

        [Test]
        public void Part1By2_IsBijective_OverFull10BitRange()
        {
            var seen = new bool[1 << 30];
            for (uint value = 0; value <= MortonCoder.AxisMax; value++)
            {
                uint spread = MortonCoder.Part1By2(value);
                Assert.That(seen[spread], Is.False, $"Part1By2 在 {value} 处发生位冲突: {spread}");
                seen[spread] = true;
            }
        }

        [Test]
        public void Part1By1_PlacesBitsAtEverySecondPosition()
        {
            Assert.That(MortonCoder.Part1By1(1u), Is.EqualTo(1u), "bit0 应落到 bit0");
            Assert.That(MortonCoder.Part1By1(2u), Is.EqualTo(4u), "bit1 应落到 bit2");
            Assert.That(MortonCoder.Part1By1(4u), Is.EqualTo(16u), "bit2 应落到 bit4");
            Assert.That(MortonCoder.Part1By1(8u), Is.EqualTo(64u), "bit3 应落到 bit6");
        }

        [Test]
        public void Part1By2_PlacesBitsAtEveryThirdPosition()
        {
            Assert.That(MortonCoder.Part1By2(1u), Is.EqualTo(1u), "bit0 应落到 bit0");
            Assert.That(MortonCoder.Part1By2(2u), Is.EqualTo(8u), "bit1 应落到 bit3");
            Assert.That(MortonCoder.Part1By2(4u), Is.EqualTo(64u), "bit2 应落到 bit6");
        }

        [Test]
        public void Encode3D_AdjacentPointsProduceAdjacentCodes()
        {
            var frame = new MortonFrame
            {
                Origin = float3.zero,
                InvExtent = new float3(1f),
                Dimension = CollisionDimension.XYZ,
            };

            uint low = MortonCoder.Encode(new float3(0f), frame);
            uint mid = MortonCoder.Encode(new float3(0.5f), frame);
            uint high = MortonCoder.Encode(new float3(1f), frame);

            Assert.That(low, Is.EqualTo(0u));
            Assert.That(mid, Is.GreaterThan(low));
            Assert.That(high, Is.GreaterThan(mid));
        }

        [Test]
        public void Encode2D_XY_IgnoresZAxis()
        {
            var frame = new MortonFrame
            {
                Origin = new float3(-10f),
                InvExtent = new float3(1f / 20f),
                Dimension = CollisionDimension.XY,
            };

            uint a = MortonCoder.Encode(new float3(1f, 2f, 0f), frame);
            uint b = MortonCoder.Encode(new float3(1f, 2f, 99f), frame);

            Assert.That(a, Is.EqualTo(b), "2D XY 模式下 Z 轴不得影响编码");
        }

        [Test]
        public void Encode2D_XZ_IgnoresYAxis()
        {
            var frame = new MortonFrame
            {
                Origin = new float3(-10f),
                InvExtent = new float3(1f / 20f),
                Dimension = CollisionDimension.XZ,
            };

            uint a = MortonCoder.Encode(new float3(1f, 0f, 2f), frame);
            uint b = MortonCoder.Encode(new float3(1f, 99f, 2f), frame);

            Assert.That(a, Is.EqualTo(b), "2D XZ 模式下 Y 轴不得影响编码");
        }

        [Test]
        public void Encode_SaturatesOutsideFrame()
        {
            var frame = new MortonFrame
            {
                Origin = float3.zero,
                InvExtent = new float3(1f),
                Dimension = CollisionDimension.XYZ,
            };

            uint below = MortonCoder.Encode(new float3(-5f), frame);
            uint above = MortonCoder.Encode(new float3(5f), frame);
            uint expectedMax = MortonCoder.Part1By2(MortonCoder.AxisMax)
                             | (MortonCoder.Part1By2(MortonCoder.AxisMax) << 1)
                             | (MortonCoder.Part1By2(MortonCoder.AxisMax) << 2);

            Assert.That(below, Is.EqualTo(0u), "低于量化范围应饱和到 0");
            Assert.That(above, Is.EqualTo(expectedMax), "高于量化范围应饱和到最大码");
        }

        [Test]
        public void Frame_DegenerateAxis_DoesNotProduceNaN()
        {
            // 全部实体共面（z 尺寸为 0）是最常见的退化情形，必须不产生 NaN / 除零。
            var flat = new Aabb(new float3(0f, 0f, 5f), new float3(10f, 10f, 5f));
            var frame = MortonFrame.From(flat, CollisionDimension.XYZ);

            Assert.That(float.IsNaN(frame.InvExtent.z), Is.False);
            Assert.That(frame.InvExtent.z, Is.EqualTo(0f));

            // 退化轴量化值必须稳定为 0（而非 NaN → 0xFFFFFFFF 这类垃圾码）。
            Assert.That(MortonCoder.Quantize(5f, frame.Origin.z, frame.InvExtent.z), Is.EqualTo(0u));

            // 全平面上的任意点都必须落在同一码（该轴不参与排序）。
            uint a = MortonCoder.Encode(new float3(0f, 0f, 5f), frame);
            uint b = MortonCoder.Encode(new float3(0f, 0f, 5f), frame);
            Assert.That(a, Is.EqualTo(b));
        }
    }
}
