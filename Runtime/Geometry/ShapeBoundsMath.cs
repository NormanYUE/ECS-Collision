using Unity.Mathematics;

namespace Ember.Collision
{
    /// <summary>
    /// 形状的本地空间包围盒计算（工具类，静态豁免，纯函数，Burst 可编译）。
    /// 结果写入 <c>Ember.Core.BoundingVolume</c> → 由框架既有的
    /// <c>WorldBoundsSystem</c> 换算为 <c>WorldBounds</c>，
    /// 因此碰撞体自动参与视锥剔除与空间索引，无需任何额外接线。
    /// </summary>
    public static class ShapeBoundsMath
    {
        /// <summary>
        /// 计算形状的本地 AABB（相对实体本地原点，含形状中心偏移）。
        /// </summary>
        /// <param name="collider">碰撞体。</param>
        /// <param name="dimension">维度模式；2D 时无效轴半范围归零（保证 BoundingVolume 语义正确）。</param>
        /// <param name="vertices">凸形状顶点缓冲起始指针；非凸形状可为 null。</param>
        /// <param name="center">输出本地包围盒中心。</param>
        /// <param name="extents">输出本地包围盒半范围。</param>
        public static unsafe void LocalBounds(
            in Collider collider,
            CollisionDimension dimension,
            float3* vertices,
            out float3 center,
            out float3 extents)
        {
            var p = collider.Params;
            switch (collider.Type)
            {
                case ShapeType.Sphere:
                case ShapeType.Circle:
                    center = p.Center;
                    extents = new float3(math.abs(p.Radius));
                    break;

                case ShapeType.Box:
                    center = p.Center;
                    extents = math.abs(p.Extents);
                    break;

                case ShapeType.Box2D:
                    center = p.Center;
                    float2 planarExtents = math.abs(p.Extents.xy);
                    extents = dimension == CollisionDimension.XZ
                        ? new float3(planarExtents.x, 0f, planarExtents.y)
                        : new float3(planarExtents.x, planarExtents.y, 0f);
                    break;

                case ShapeType.Capsule:
                    center = p.Center;
                    extents = CapsuleExtents(p.Radius, p.HalfHeight, p.Axis);
                    break;

                case ShapeType.Capsule2D:
                    center = p.Center;
                    extents = Capsule2DExtents(p.Radius, p.HalfHeight, p.Axis, dimension);
                    break;

                case ShapeType.Polygon2D:
                case ShapeType.Convex:
                    if (vertices == null || p.VertexCount <= 0)
                    {
                        center = p.Center;
                        extents = float3.zero;
                        break;
                    }

                    ConvexBounds(vertices, p.VertexCount, out float3 vertexCenter, out extents);
                    center = p.Center + vertexCenter;
                    break;

                default:
                    center = p.Center;
                    extents = float3.zero;
                    break;
            }

            if (dimension == CollisionDimension.XY)
                extents = new float3(extents.x, extents.y, 0f);
            else if (dimension == CollisionDimension.XZ)
                extents = new float3(extents.x, 0f, extents.z);
        }

        /// <summary>胶囊半范围：沿轴为 halfHeight + radius，垂直于轴为 radius。</summary>
        public static float3 CapsuleExtents(float radius, float halfHeight, byte axis)
        {
            float shortHalf = math.abs(radius);
            float longHalf = math.abs(halfHeight) + shortHalf;
            switch (axis)
            {
                case 0: return new float3(longHalf, shortHalf, shortHalf);
                case 2: return new float3(shortHalf, shortHalf, longHalf);
                default: return new float3(shortHalf, longHalf, shortHalf);
            }
        }

        /// <summary>
        /// 2D 胶囊的参数轴使用平面 U/V：U 始终映射 X，V 在 XY 映射 Y、在 XZ 映射 Z。
        /// </summary>
        public static float3 Capsule2DExtents(
            float radius, float halfHeight, byte axis, CollisionDimension dimension)
        {
            float shortHalf = math.abs(radius);
            float longHalf = shortHalf + math.abs(halfHeight);
            bool alongU = axis == 0;

            if (dimension == CollisionDimension.XZ)
                return alongU
                    ? new float3(longHalf, 0f, shortHalf)
                    : new float3(shortHalf, 0f, longHalf);

            return alongU
                ? new float3(longHalf, shortHalf, 0f)
                : new float3(shortHalf, longHalf, 0f);
        }

        /// <summary>从顶点集计算紧致 AABB 半范围（相对于顶点集中心）。</summary>
        public static unsafe float3 ConvexExtents(float3* vertices, int count)
        {
            ConvexBounds(vertices, count, out _, out float3 extents);
            return extents;
        }

        /// <summary>从顶点集计算相对于顶点原点的中心和半范围。</summary>
        public static unsafe void ConvexBounds(
            float3* vertices, int count, out float3 center, out float3 extents)
        {
            float3 min = vertices[0];
            float3 max = vertices[0];
            for (int i = 1; i < count; i++)
            {
                min = math.min(min, vertices[i]);
                max = math.max(max, vertices[i]);
            }

            center = (min + max) * 0.5f;
            extents = (max - min) * 0.5f;
        }

        /// <summary>
        /// 从 <c>LocalToWorld</c> 与本地 AABB 求世界 AABB。
        /// ABS 轴投影法（与 <c>Ember.Core.FrustumMath.TransformAABB</c> 同算法），
        /// 结果紧致且不漏包；自带实现以便在 Burst Job 内使用并支持无效轴撑开。
        /// </summary>
        public static void ToWorldBounds(
            in float4x4 localToWorld,
            float3 localCenter,
            float3 localExtents,
            out float3 worldCenter,
            out float3 worldExtents)
        {
            worldCenter = math.transform(localToWorld, localCenter);
            worldExtents = math.abs(localToWorld.c0.xyz) * localExtents.x
                         + math.abs(localToWorld.c1.xyz) * localExtents.y
                         + math.abs(localToWorld.c2.xyz) * localExtents.z;
        }

        /// <summary>
        /// 从 4x4 矩阵分解出等比缩放（用于把本地形状尺寸送到世界空间）。
        /// 返回三个轴长；非等比缩放会取各轴独立长度，由调用方决定如何降级。
        /// </summary>
        public static float3 AxisLengths(in float4x4 localToWorld) => new(
            math.length(localToWorld.c0.xyz),
            math.length(localToWorld.c1.xyz),
            math.length(localToWorld.c2.xyz));

        /// <summary>
        /// 把本地形状尺寸按矩阵轴长换算为世界空间尺寸，并撑开 2D 无效轴。
        /// 仅适用于球 / 圆 / 盒这类轴对齐尺寸；胶囊与凸形状走各自专用路径。
        /// </summary>
        public static void WorldShapeExtents(
            in float4x4 localToWorld,
            float3 localExtents,
            CollisionDimension dimension,
            out float3 worldExtents)
        {
            float3 axisLengths = AxisLengths(localToWorld);
            worldExtents = math.abs(axisLengths) * localExtents;
            if (dimension != CollisionDimension.XYZ)
                worldExtents = InactiveAxis(dimension, worldExtents);
        }

        private static float3 InactiveAxis(CollisionDimension dimension, float3 extents) =>
            dimension == CollisionDimension.XY
                ? new float3(extents.x, extents.y, Aabb.InactiveAxisHalfExtent)
                : new float3(extents.x, Aabb.InactiveAxisHalfExtent, extents.z);
    }
}
