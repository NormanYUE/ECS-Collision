using Unity.Mathematics;

namespace Ember.Collision
{
    /// <summary>
    /// 轴对齐包围盒（世界空间）。2D 模式下无效轴会被撑开到
    /// ±<see cref="InactiveAxisHalfExtent"/>，使通用 3D 重叠测试自动退化为 2D 语义
    /// （与 <c>Ember.Core.FrustumMath</c> 的稀疏轴处理思路一致）。
    /// </summary>
    public struct Aabb
    {
        /// <summary>2D 模式无效轴的半范围。</summary>
        public const float InactiveAxisHalfExtent = 1e9f;

        /// <summary>最小角。</summary>
        public float3 Min;

        /// <summary>最大角。</summary>
        public float3 Max;

        /// <summary>构造。</summary>
        public Aabb(float3 min, float3 max)
        {
            Min = min;
            Max = max;
        }

        /// <summary>由中心 + 半范围构造。</summary>
        public static Aabb FromCenterExtents(float3 center, float3 extents) =>
            new(center - extents, center + extents);

        /// <summary>无效包围盒（空集），用于 Encapsulate 的初始值。</summary>
        public static Aabb Empty => new(new float3(float.PositiveInfinity), new float3(float.NegativeInfinity));

        /// <summary>2D 模式下把无效轴撑开，得到「仅两个轴有效」的包围盒。</summary>
        public static Aabb InflateInactiveAxis(in Aabb box, CollisionDimension dimension)
        {
            switch (dimension)
            {
                case CollisionDimension.XY:
                    return new Aabb(
                        new float3(box.Min.x, box.Min.y, -InactiveAxisHalfExtent),
                        new float3(box.Max.x, box.Max.y, InactiveAxisHalfExtent));
                case CollisionDimension.XZ:
                    return new Aabb(
                        new float3(box.Min.x, -InactiveAxisHalfExtent, box.Min.z),
                        new float3(box.Max.x, InactiveAxisHalfExtent, box.Max.z));
                default:
                    return box;
            }
        }

        /// <summary>中心。</summary>
        public readonly float3 Center => (Min + Max) * 0.5f;

        /// <summary>半范围。</summary>
        public readonly float3 Extents => (Max - Min) * 0.5f;

        /// <summary>单轴长度。</summary>
        public readonly float3 Size => Max - Min;

        /// <summary>是否为空集。</summary>
        public readonly bool IsEmpty => !(Min.x <= Max.x && Min.y <= Max.y && Min.z <= Max.z);

        /// <summary>闭区间重叠测试（含边界接触）。</summary>
        public static bool Overlaps(in Aabb a, in Aabb b) =>
            a.Min.x <= b.Max.x && a.Max.x >= b.Min.x &&
            a.Min.y <= b.Max.y && a.Max.y >= b.Min.y &&
            a.Min.z <= b.Max.z && a.Max.z >= b.Min.z;

        /// <summary>是否完全包含另一包围盒。</summary>
        public static bool Contains(in Aabb outer, in Aabb inner) =>
            outer.Min.x <= inner.Min.x && outer.Max.x >= inner.Max.x &&
            outer.Min.y <= inner.Min.y && outer.Max.y >= inner.Max.y &&
            outer.Min.z <= inner.Min.z && outer.Max.z >= inner.Max.z;

        /// <summary>是否包含点。</summary>
        public readonly bool Contains(float3 point) =>
            point.x >= Min.x && point.x <= Max.x &&
            point.y >= Min.y && point.y <= Max.y &&
            point.z >= Min.z && point.z <= Max.z;

        /// <summary>扩张到包含另一点。</summary>
        public void Encapsulate(float3 point)
        {
            Min = math.min(Min, point);
            Max = math.max(Max, point);
        }

        /// <summary>扩张到包含另一包围盒。</summary>
        public void Encapsulate(in Aabb other)
        {
            Min = math.min(Min, other.Min);
            Max = math.max(Max, other.Max);
        }

        /// <summary>按统一边距外扩。</summary>
        public void Expand(float margin)
        {
            Min -= margin;
            Max += margin;
        }

        /// <summary>表面积（用于 SAH 代价估计）。</summary>
        public readonly float SurfaceArea
        {
            get
            {
                float3 d = Max - Min;
                return 2f * (d.x * d.y + d.y * d.z + d.z * d.x);
            }
        }

        /// <summary>并集。</summary>
        public static Aabb Union(in Aabb a, in Aabb b) =>
            new(math.min(a.Min, b.Min), math.max(a.Max, b.Max));

        /// <summary>相交测试，并输出交集体积（用于粗略剪枝）。</summary>
        public static bool OverlapsVolume(in Aabb a, in Aabb b, out float volume)
        {
            float3 lo = math.max(a.Min, b.Min);
            float3 hi = math.min(a.Max, b.Max);
            float3 d = hi - lo;
            if (d.x < 0f || d.y < 0f || d.z < 0f)
            {
                volume = 0f;
                return false;
            }

            volume = d.x * d.y * d.z;
            return true;
        }
    }
}
