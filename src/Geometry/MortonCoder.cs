using Unity.Mathematics;

namespace Ember.Collision
{
    /// <summary>
    /// Morton 码量化坐标系：把世界空间 AABB 映射到 [0,1]^3 后量化到每轴 10 位。
    /// 由本帧全体碰撞体的总包围盒（含边距）构造一次，供全体 Job 共享。
    /// </summary>
    public struct MortonFrame
    {
        /// <summary>量化原点（总包围盒最小角）。</summary>
        public float3 Origin;

        /// <summary>每世界单位的量化倒数（1 / 总包围盒尺寸，逐轴）。</summary>
        public float3 InvExtent;

        /// <summary>维度模式，决定参与量化的轴。</summary>
        public CollisionDimension Dimension;

        /// <summary>由总包围盒构造量化坐标系；退化轴（尺寸为 0）时该轴恒量化为 0。</summary>
        public static MortonFrame From(in Aabb total, CollisionDimension dimension)
        {
            float3 size = total.Max - total.Min;
            const float epsilon = 1e-6f;
            float3 inv = new(
                size.x > epsilon ? 1f / size.x : 0f,
                size.y > epsilon ? 1f / size.y : 0f,
                size.z > epsilon ? 1f / size.z : 0f);
            return new MortonFrame
            {
                Origin = total.Min,
                InvExtent = inv,
                Dimension = dimension,
            };
        }
    }

    /// <summary>
    /// Morton 码（Z-order 曲线）编码器，2D / 3D 统一（工具类，静态豁免，纯函数）。
    ///
    /// 2D 模式只交错两个激活轴（XY 或 XZ），因此 2D 世界的排序质量与 3D 一致，
    /// 不会因为无效轴的噪声而退化。
    /// </summary>
    public static class MortonCoder
    {
        /// <summary>每轴量化位数。</summary>
        public const int BitsPerAxis = 10;

        /// <summary>每轴最大值。</summary>
        public const int AxisMax = (1 << BitsPerAxis) - 1;

        /// <summary>把 10 位输入散布到每隔 3 位（3D 用）。</summary>
        public static uint Part1By2(uint value)
        {
            value &= 0x000003ffu;
            value = (value ^ (value << 16)) & 0xff0000ffu;
            value = (value ^ (value << 8)) & 0x0300f00fu;
            value = (value ^ (value << 4)) & 0x030c30c3u;
            value = (value ^ (value << 2)) & 0x09249249u;
            return value;
        }

        /// <summary>把 10 位输入散布到每隔 1 位（2D 用）。</summary>
        public static uint Part1By1(uint value)
        {
            value &= 0x000003ffu;
            value = (value ^ (value << 8)) & 0x00ff00ffu;
            value = (value ^ (value << 4)) & 0x0f0f0f0fu;
            value = (value ^ (value << 2)) & 0x33333333u;
            value = (value ^ (value << 1)) & 0x55555555u;
            return value;
        }

        /// <summary>把归一化坐标量化为 10 位无符号整数。</summary>
        public static uint Quantize(float value, float origin, float invExtent)
        {
            float normalized = (value - origin) * invExtent;
            float scaled = math.saturate(normalized) * AxisMax;
            return (uint)(scaled + 0.5f);
        }

        /// <summary>
        /// 用包围盒中心编码 Morton 码。中心比最小角更能代表体素归属，
        /// 大幅重叠的盒体排序质量更稳定。
        /// </summary>
        public static uint Encode(float3 point, in MortonFrame frame)
        {
            switch (frame.Dimension)
            {
                case CollisionDimension.XY:
                {
                    uint x = Quantize(point.x, frame.Origin.x, frame.InvExtent.x);
                    uint y = Quantize(point.y, frame.Origin.y, frame.InvExtent.y);
                    return Part1By1(x) | (Part1By1(y) << 1);
                }
                case CollisionDimension.XZ:
                {
                    uint x = Quantize(point.x, frame.Origin.x, frame.InvExtent.x);
                    uint z = Quantize(point.z, frame.Origin.z, frame.InvExtent.z);
                    return Part1By1(x) | (Part1By1(z) << 1);
                }
                default:
                {
                    uint x = Quantize(point.x, frame.Origin.x, frame.InvExtent.x);
                    uint y = Quantize(point.y, frame.Origin.y, frame.InvExtent.y);
                    uint z = Quantize(point.z, frame.Origin.z, frame.InvExtent.z);
                    return Part1By2(x) | (Part1By2(y) << 1) | (Part1By2(z) << 2);
                }
            }
        }

        /// <summary>用包围盒编码（取中心点）。</summary>
        public static uint Encode(in Aabb bounds, in MortonFrame frame) => Encode(bounds.Center, frame);
    }
}
