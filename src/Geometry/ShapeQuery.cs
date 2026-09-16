using Unity.Mathematics;

namespace Ember.Collision
{
    /// <summary>
    /// 形状级几何查询（N1）：shape → point 最近点 / 距离。
    /// 供 Ember.Navigation 烘焙核心（体素距离场）等消费方使用。
    ///
    /// 语义约定：
    /// <list type="bullet">
    ///   <item>返回值为<b>有符号距离</b> —— 点在形状外为正、内为负（-穿透深度）、表面上为 0。</item>
    ///   <item><paramref name="normal"/> 为从最近点指向查询点的单位向量；点在内部时指向最近的外表面方向。</item>
    ///   <item>2D 形状的最近点与法线按 <paramref name="dimension"/> 语义抬升到 3D
    ///   （无效轴取查询点的对应坐标，与窄相 Lift 模式一致）。</item>
    ///   <item>Burst 兼容纯静态函数，无托管分配。</item>
    /// </list>
    /// </summary>
    public static unsafe class ShapeQuery
    {
        private const float Epsilon = 1e-6f;

        /// <summary>
        /// 计算查询点到碰撞体表面的最近点。覆盖全部 7 种 <see cref="ShapeType"/>；
        /// 3D 形状忽略 <paramref name="dimension"/> 与顶点池。
        /// </summary>
        /// <param name="collider">碰撞体（形状 + 参数）。</param>
        /// <param name="pose">世界位姿（等比缩放语义同窄相）。</param>
        /// <param name="dimension">2D 形状的平面语义（XY / XZ）；3D 形状传 XYZ。</param>
        /// <param name="point">世界空间查询点。</param>
        /// <param name="vertexPool">凸多边形顶点池（局部平面坐标）；非 Polygon2D 传 null。</param>
        /// <param name="vertexPoolLength">顶点池长度（越界访问防护）。</param>
        /// <param name="closest">世界空间最近点。</param>
        /// <param name="normal">最近点 → 查询点的单位向量。</param>
        /// <returns>有符号距离（外正内负）。</returns>
        public static float ClosestPoint(
            in Collider collider,
            in BodyPose pose,
            CollisionDimension dimension,
            float3 point,
            float3* vertexPool,
            int vertexPoolLength,
            out float3 closest,
            out float3 normal)
        {
            switch (collider.Type)
            {
                case ShapeType.Sphere:
                case ShapeType.Capsule:
                case ShapeType.Box:
                    return ClosestPoint3D(collider, pose, point, out closest, out normal);

                case ShapeType.Circle:
                case ShapeType.Box2D:
                case ShapeType.Capsule2D:
                case ShapeType.Polygon2D:
                    return ClosestPoint2D(collider, pose, dimension, point, vertexPool, vertexPoolLength,
                        out closest, out normal);

                default:
                    closest = point;
                    normal = new float3(1f, 0f, 0f);
                    return 0f;
            }
        }

        /// <summary>非多边形形状的便捷重载（无需顶点池）。Polygon2D 调用此方抛参数异常。</summary>
        public static float ClosestPoint(
            in Collider collider,
            in BodyPose pose,
            CollisionDimension dimension,
            float3 point,
            out float3 closest,
            out float3 normal)
        {
            if (collider.Type == ShapeType.Polygon2D)
                throw new System.ArgumentException(
                    "ShapeQuery.ClosestPoint: Polygon2D requires the vertex pool overload.", nameof(collider));
            return ClosestPoint(collider, pose, dimension, point, null, 0, out closest, out normal);
        }

        private static float ClosestPoint3D(
            in Collider collider, in BodyPose pose, float3 point, out float3 closest, out float3 normal)
        {
            switch (collider.Type)
            {
                case ShapeType.Sphere:
                {
                    NarrowphaseMath.Sphere3 sphere = NarrowphaseMath.BuildSphere(collider, pose);
                    return ClosestOnSphere(point, sphere.Center, sphere.Radius, out closest, out normal);
                }

                case ShapeType.Capsule:
                {
                    NarrowphaseMath.Capsule3 capsule = NarrowphaseMath.BuildCapsule(collider, pose);
                    float3 axisClosest = NarrowphaseMath.ClosestPointOnSegment(point, capsule.Start, capsule.End);
                    return ClosestOnSphere(point, axisClosest, capsule.Radius, out closest, out normal);
                }

                case ShapeType.Box:
                {
                    NarrowphaseMath.Box3 box = NarrowphaseMath.BuildBox(collider, pose);
                    float3 local = NarrowphaseMath.ToLocal(box, point);
                    float3 clamped = math.clamp(local, -box.Extents, box.Extents);

                    if (math.all(math.abs(local) <= box.Extents))
                    {
                        // 点在盒内：沿穿透最浅的轴推到外表面。
                        float depthX = box.Extents.x - math.abs(local.x);
                        float depthY = box.Extents.y - math.abs(local.y);
                        float depthZ = box.Extents.z - math.abs(local.z);
                        closest = local;
                        float3 axis;
                        float depth;
                        if (depthX <= depthY && depthX <= depthZ)
                        {
                            depth = depthX;
                            axis = new float3(local.x >= 0f ? 1f : -1f, 0f, 0f);
                            closest.x = axis.x * box.Extents.x;
                        }
                        else if (depthY <= depthZ)
                        {
                            depth = depthY;
                            axis = new float3(0f, local.y >= 0f ? 1f : -1f, 0f);
                            closest.y = axis.y * box.Extents.y;
                        }
                        else
                        {
                            depth = depthZ;
                            axis = new float3(0f, 0f, local.z >= 0f ? 1f : -1f);
                            closest.z = axis.z * box.Extents.z;
                        }
                        normal = NarrowphaseMath.NormalizeOr(
                            box.AxisX * axis.x + box.AxisY * axis.y + box.AxisZ * axis.z,
                            new float3(1f, 0f, 0f));
                        closest = NarrowphaseMath.FromLocal(box, closest);
                        return -depth;
                    }

                    float3 delta = local - clamped;
                    float distance = math.length(delta);
                    normal = NarrowphaseMath.NormalizeOr(
                        box.AxisX * delta.x + box.AxisY * delta.y + box.AxisZ * delta.z,
                        new float3(1f, 0f, 0f));
                    closest = NarrowphaseMath.FromLocal(box, clamped);
                    return distance;
                }

                default:
                    closest = point;
                    normal = new float3(1f, 0f, 0f);
                    return 0f;
            }
        }

        private static float ClosestPoint2D(
            in Collider collider,
            in BodyPose pose,
            CollisionDimension dimension,
            float3 point,
            float3* vertexPool,
            int vertexPoolLength,
            out float3 closest,
            out float3 normal)
        {
            var planar = new NarrowphaseMath.PlanarShape
            {
                Collider = collider,
                Pose = pose,
                Dimension = dimension,
                VertexPool = vertexPool,
                VertexPoolLength = vertexPoolLength,
            };

            float inactiveCoordinate = dimension == CollisionDimension.XZ ? point.y : point.z;
            float2 query = NarrowphaseMath.Project(point, dimension);
            float signedDistance;
            float2 closest2;
            float2 normal2;

            switch (collider.Type)
            {
                case ShapeType.Circle:
                {
                    NarrowphaseMath.Circle2 circle = NarrowphaseMath.BuildCircle2(planar);
                    signedDistance = ClosestOnCircle2(query, circle.Center, circle.Radius, out closest2, out normal2);
                    break;
                }

                case ShapeType.Capsule2D:
                {
                    NarrowphaseMath.Capsule2 capsule = NarrowphaseMath.BuildCapsule2(planar);
                    float2 axisClosest = NarrowphaseMath.ClosestPointOnSegment2D(query, capsule.Start, capsule.End);
                    signedDistance = ClosestOnCircle2(query, axisClosest, capsule.Radius, out closest2, out normal2);
                    break;
                }

                case ShapeType.Box2D:
                {
                    NarrowphaseMath.Box2 box = NarrowphaseMath.BuildBox2(planar);
                    signedDistance = ClosestOnBox2(query, box, out closest2, out normal2);
                    break;
                }

                case ShapeType.Polygon2D:
                {
                    if (!NarrowphaseMath.IsValidPlanarShape(planar))
                    {
                        // 退化多边形（顶点数 < 3 或池缺失）：退化为质心点查询，行为明确。
                        float2 center = NarrowphaseMath.Center2(planar);
                        signedDistance = ClosestOnCircle2(query, center, 0f, out closest2, out normal2);
                        break;
                    }
                    signedDistance = ClosestOnPolygon2(planar, query, out closest2, out normal2);
                    break;
                }

                default:
                    closest2 = query;
                    normal2 = new float2(1f, 0f);
                    signedDistance = 0f;
                    break;
            }

            closest = NarrowphaseMath.LiftPoint(closest2, dimension, inactiveCoordinate);
            normal = NarrowphaseMath.LiftDirection(normal2, dimension);
            return signedDistance;
        }

        /// <summary>径向形状的公共收尾：球 / 圆 / 胶囊截面共用「最近点 = 中心 + 半径 × 方向」。</summary>
        private static float ClosestOnSphere(
            float3 point, float3 center, float radius, out float3 closest, out float3 normal)
        {
            float3 delta = point - center;
            float distance = math.length(delta);
            normal = distance > Epsilon
                ? delta / distance
                : new float3(1f, 0f, 0f);
            closest = center + normal * radius;
            return distance - radius;
        }

        private static float ClosestOnCircle2(
            float2 point, float2 center, float radius, out float2 closest, out float2 normal)
        {
            float2 delta = point - center;
            float distance = math.length(delta);
            normal = distance > Epsilon
                ? delta / distance
                : new float2(1f, 0f);
            closest = center + normal * radius;
            return distance - radius;
        }

        private static float ClosestOnBox2(
            float2 query, in NarrowphaseMath.Box2 box, out float2 closest, out float2 normal)
        {
            float2 local = NarrowphaseMath.ToLocal2(box, query);
            float2 clamped = math.clamp(local, -box.Extents, box.Extents);

            if (math.all(math.abs(local) <= box.Extents))
            {
                float depthU = box.Extents.x - math.abs(local.x);
                float depthV = box.Extents.y - math.abs(local.y);
                closest = local;
                float2 axis;
                float depth;
                if (depthU <= depthV)
                {
                    depth = depthU;
                    axis = new float2(local.x >= 0f ? 1f : -1f, 0f);
                    closest.x = axis.x * box.Extents.x;
                }
                else
                {
                    depth = depthV;
                    axis = new float2(0f, local.y >= 0f ? 1f : -1f);
                    closest.y = axis.y * box.Extents.y;
                }
                normal = NarrowphaseMath.NormalizeOr2(
                    box.AxisU * axis.x + box.AxisV * axis.y,
                    new float2(1f, 0f));
                closest = NarrowphaseMath.FromLocal2(box, closest);
                return -depth;
            }

            float2 delta = local - clamped;
            float distance = math.length(delta);
            normal = NarrowphaseMath.NormalizeOr2(
                box.AxisU * delta.x + box.AxisV * delta.y,
                new float2(1f, 0f));
            closest = NarrowphaseMath.FromLocal2(box, clamped);
            return distance;
        }

        /// <summary>
        /// 凸多边形最近点：逐边取线段最近点取最小距离；点在内部时取最近边。
        /// 顶点顺序不要求（凸性由烘焙侧保证），inside 判定用全边叉积同号。
        /// </summary>
        private static float ClosestOnPolygon2(
            in NarrowphaseMath.PlanarShape shape, float2 query, out float2 closest, out float2 normal)
        {
            int start = shape.Collider.Params.VertexStart;
            int count = shape.Collider.Params.VertexCount;

            float bestDistanceSq = float.MaxValue;
            float2 bestClosest = default;
            float2 bestEdgeNormal = new float2(1f, 0f);
            bool inside = true;
            float sign = 0f;

            for (int i = 0; i < count; i++)
            {
                float3 localA = shape.Collider.Params.Center + shape.VertexPool[start + i];
                float3 localB = shape.Collider.Params.Center
                    + shape.VertexPool[start + (i + 1) % count];
                float2 a = NarrowphaseMath.Project(shape.Pose.TransformPoint(localA), shape.Dimension);
                float2 b = NarrowphaseMath.Project(shape.Pose.TransformPoint(localB), shape.Dimension);

                float2 edge = b - a;
                float2 toQuery = query - a;
                float edgeLengthSq = math.lengthsq(edge);
                float t = edgeLengthSq > Epsilon
                    ? math.clamp(math.dot(toQuery, edge) / edgeLengthSq, 0f, 1f)
                    : 0f;
                float2 candidate = a + edge * t;
                float distanceSq = math.lengthsq(query - candidate);
                if (distanceSq < bestDistanceSq)
                {
                    bestDistanceSq = distanceSq;
                    bestClosest = candidate;
                    float2 edgeDir = edgeLengthSq > Epsilon ? edge * math.rsqrt(edgeLengthSq) : new float2(1f, 0f);
                    // 边法线取指向查询点的一侧。
                    float2 candidateToQuery = query - candidate;
                    float2 n = new float2(-edgeDir.y, edgeDir.x);
                    bestEdgeNormal = math.dot(n, candidateToQuery) >= 0f ? n : -n;
                }

                // 内部判定：所有边的叉积同号（凸多边形）。
                float cross = edge.x * toQuery.y - edge.y * toQuery.x;
                if (math.abs(cross) > Epsilon)
                {
                    float s = math.sign(cross);
                    if (sign == 0f) sign = s;
                    else if (s != sign) inside = false;
                }
            }

            closest = bestClosest;
            float distance = math.sqrt(bestDistanceSq);
            if (inside)
            {
                normal = NarrowphaseMath.NormalizeOr2(bestEdgeNormal, new float2(1f, 0f));
                return -distance;
            }
            normal = distance > Epsilon
                ? (query - bestClosest) / distance
                : NarrowphaseMath.NormalizeOr2(bestEdgeNormal, new float2(1f, 0f));
            return distance;
        }
    }
}
