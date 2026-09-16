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

        /// <summary>
        /// 射线与单个形状的最近相交（N3）。覆盖全部 7 种 <see cref="ShapeType"/>。
        ///
        /// 语义约定：
        /// <list type="bullet">
        ///   <item><paramref name="direction"/> 无需归一化，实现内部归一化；零向量视为不命中。</item>
        ///   <item>起点在形状内或表面上（<see cref="ClosestPoint"/> 有符号距离 ≤ 0）视为命中：
        ///   <c>distance = 0</c>、<c>point = origin</c>、<c>normal</c> 与 <see cref="ClosestPoint"/>
        ///   同口径（由最近点指向起点；起点在内时该法线朝内）。</item>
        ///   <item>命中参数落在 <c>[0, maxDistance]</c> 之外视为不命中；<paramref name="maxDistance"/>
        ///   为负直接不命中；为 0 时仅「起点在形状内」算命中。</item>
        ///   <item>不命中时 <c>distance = 0</c>、<c>point = origin</c>、<c>normal = float3.zero</c>。</item>
        ///   <item>相切（判别式为 0）算命中。</item>
        ///   <item>2D 形状按「沿无效轴无限延伸的棱柱」处理，在 <paramref name="dimension"/> 平面内求解；
        ///   <c>point</c> 取射线上对应参数处的真实坐标（含无效轴分量），与 <see cref="ClosestPoint"/> 的
        ///   抬升口径一致。射线垂直于平面时无有限交点，返回不命中。</item>
        ///   <item>Burst 兼容纯静态函数，无托管分配。</item>
        /// </list>
        /// </summary>
        /// <param name="collider">碰撞体（形状 + 参数）。</param>
        /// <param name="pose">世界位姿（等比缩放语义同窄相）。</param>
        /// <param name="dimension">2D 形状的平面语义（XY / XZ）；3D 形状传 XYZ。</param>
        /// <param name="origin">世界空间射线起点。</param>
        /// <param name="direction">世界空间射线方向（无需归一化）。</param>
        /// <param name="maxDistance">世界空间最大命中距离。</param>
        /// <param name="vertexPool">凸多边形顶点池（局部平面坐标）；非 Polygon2D 传 null。</param>
        /// <param name="vertexPoolLength">顶点池长度（越界访问防护）。</param>
        /// <param name="distance">命中处沿射线方向的距离（起点在形状内时为 0）。</param>
        /// <param name="point">世界空间命中点。</param>
        /// <param name="normal">命中点处指向射线来向的单位外法线。</param>
        /// <returns>是否命中。</returns>
        public static bool Raycast(
            in Collider collider,
            in BodyPose pose,
            CollisionDimension dimension,
            float3 origin,
            float3 direction,
            float maxDistance,
            float3* vertexPool,
            int vertexPoolLength,
            out float distance,
            out float3 point,
            out float3 normal)
        {
            distance = 0f;
            point = origin;
            normal = float3.zero;

            if (collider.Type == ShapeType.None) return false;
            float directionLengthSq = math.lengthsq(direction);
            if (directionLengthSq <= Epsilon * Epsilon) return false;
            if (maxDistance < 0f) return false;
            float3 unitDirection = direction * math.rsqrt(directionLengthSq);

            // 起点在形状内或表面上：统一在此给出确定性结果，各求解器只需处理严格外部起点。
            if (ClosestPoint(collider, pose, dimension, origin, vertexPool, vertexPoolLength,
                    out float3 _, out float3 insideNormal) <= 0f)
            {
                normal = insideNormal;
                return true;
            }

            float hitDistance;
            float3 hitNormal;
            switch (collider.Type)
            {
                case ShapeType.Sphere:
                case ShapeType.Capsule:
                case ShapeType.Box:
                    if (!Raycast3D(collider, pose, origin, unitDirection, maxDistance,
                            out hitDistance, out hitNormal))
                        return false;
                    break;

                case ShapeType.Circle:
                case ShapeType.Box2D:
                case ShapeType.Capsule2D:
                case ShapeType.Polygon2D:
                    if (!Raycast2D(collider, pose, dimension, origin, unitDirection, maxDistance,
                            vertexPool, vertexPoolLength, out hitDistance, out hitNormal))
                        return false;
                    break;

                default:
                    return false;
            }

            distance = hitDistance;
            normal = hitNormal;
            point = origin + unitDirection * hitDistance;
            return true;
        }

        /// <summary>非多边形形状的便捷重载（无需顶点池）。Polygon2D 调用此方抛参数异常。</summary>
        public static bool Raycast(
            in Collider collider,
            in BodyPose pose,
            CollisionDimension dimension,
            float3 origin,
            float3 direction,
            float maxDistance,
            out float distance,
            out float3 point,
            out float3 normal)
        {
            if (collider.Type == ShapeType.Polygon2D)
                throw new System.ArgumentException(
                    "ShapeQuery.Raycast: Polygon2D requires the vertex pool overload.", nameof(collider));
            return Raycast(collider, pose, dimension, origin, direction, maxDistance,
                null, 0, out distance, out point, out normal);
        }

        /// <summary>3D 便捷重载（<see cref="CollisionDimension.XYZ"/>）。</summary>
        public static bool Raycast(
            in Collider collider,
            in BodyPose pose,
            float3 origin,
            float3 direction,
            float maxDistance,
            out float distance,
            out float3 point,
            out float3 normal) =>
            Raycast(collider, pose, CollisionDimension.XYZ, origin, direction, maxDistance,
                out distance, out point, out normal);

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

        // ---- 射线求解（N3）：以下均假设 direction 已归一化、且起点在形状外 ----

        private static bool Raycast3D(
            in Collider collider, in BodyPose pose, float3 origin, float3 direction, float maxDistance,
            out float distance, out float3 normal)
        {
            distance = 0f;
            normal = float3.zero;

            switch (collider.Type)
            {
                case ShapeType.Sphere:
                {
                    NarrowphaseMath.Sphere3 sphere = NarrowphaseMath.BuildSphere(collider, pose);
                    if (!RaySphere(origin, direction, sphere.Center, sphere.Radius, maxDistance, out distance))
                        return false;
                    normal = NarrowphaseMath.NormalizeOr(
                        origin + direction * distance - sphere.Center, -direction);
                    return true;
                }

                case ShapeType.Capsule:
                {
                    NarrowphaseMath.Capsule3 capsule = NarrowphaseMath.BuildCapsule(collider, pose);
                    if (!RayCapsule(origin, direction, capsule.Start, capsule.End, capsule.Radius,
                            maxDistance, out distance))
                        return false;
                    float3 hit = origin + direction * distance;
                    float3 axisPoint = NarrowphaseMath.ClosestPointOnSegment(hit, capsule.Start, capsule.End);
                    normal = NarrowphaseMath.NormalizeOr(hit - axisPoint, -direction);
                    return true;
                }

                case ShapeType.Box:
                {
                    NarrowphaseMath.Box3 box = NarrowphaseMath.BuildBox(collider, pose);
                    return RayBox(origin, direction, box, maxDistance, out distance, out normal);
                }

                default:
                    return false;
            }
        }

        /// <summary>
        /// 2D 形状抬升到 3D 求解：把平面内几何与射线按<b>射线自身的无效轴坐标</b>抬起，
        /// 于是 2D 形状与其棱柱在平面内的截线完全一致，可直接复用 3D 求解器。
        /// 求解用平面内单位方向，参数需按 <c>t_world = t_planar / |project(direction)|</c> 换算回世界参数。
        /// </summary>
        private static bool Raycast2D(
            in Collider collider, in BodyPose pose, CollisionDimension dimension,
            float3 origin, float3 direction, float maxDistance,
            float3* vertexPool, int vertexPoolLength,
            out float distance, out float3 normal)
        {
            distance = 0f;
            normal = float3.zero;

            var planar = new NarrowphaseMath.PlanarShape
            {
                Collider = collider,
                Pose = pose,
                Dimension = dimension,
                VertexPool = vertexPool,
                VertexPoolLength = vertexPoolLength,
            };
            if (!NarrowphaseMath.IsValidPlanarShape(planar)) return false;

            float2 direction2 = NarrowphaseMath.Project(direction, dimension);
            float direction2LengthSq = math.lengthsq(direction2);
            // 射线垂直于平面：2D 形状是沿无效轴无限延伸的棱柱，此时无有限交点。
            if (direction2LengthSq <= Epsilon * Epsilon) return false;
            float direction2Length = math.sqrt(direction2LengthSq);

            float2 origin2 = NarrowphaseMath.Project(origin, dimension);
            float inactive = dimension == CollisionDimension.XZ ? origin.y : origin.z;
            float3 rayOrigin = NarrowphaseMath.LiftPoint(origin2, dimension, inactive);
            float3 rayDirection = NarrowphaseMath.LiftDirection(direction2 / direction2Length, dimension);
            float planarMaxDistance = maxDistance * direction2Length;

            float planarDistance;
            switch (collider.Type)
            {
                case ShapeType.Circle:
                {
                    NarrowphaseMath.Circle2 circle = NarrowphaseMath.BuildCircle2(planar);
                    float3 center = NarrowphaseMath.LiftPoint(circle.Center, dimension, inactive);
                    if (!RaySphere(rayOrigin, rayDirection, center, circle.Radius,
                            planarMaxDistance, out planarDistance))
                        return false;
                    normal = NarrowphaseMath.NormalizeOr(
                        rayOrigin + rayDirection * planarDistance - center, -rayDirection);
                    break;
                }

                case ShapeType.Capsule2D:
                {
                    NarrowphaseMath.Capsule2 capsule = NarrowphaseMath.BuildCapsule2(planar);
                    float3 start = NarrowphaseMath.LiftPoint(capsule.Start, dimension, inactive);
                    float3 end = NarrowphaseMath.LiftPoint(capsule.End, dimension, inactive);
                    if (!RayCapsule(rayOrigin, rayDirection, start, end, capsule.Radius,
                            planarMaxDistance, out planarDistance))
                        return false;
                    float3 hit = rayOrigin + rayDirection * planarDistance;
                    normal = NarrowphaseMath.NormalizeOr(
                        hit - NarrowphaseMath.ClosestPointOnSegment(hit, start, end), -rayDirection);
                    break;
                }

                case ShapeType.Box2D:
                {
                    NarrowphaseMath.Box2 box = NarrowphaseMath.BuildBox2(planar);
                    float3 axisX = NarrowphaseMath.LiftDirection(box.AxisU, dimension);
                    float3 axisY = NarrowphaseMath.LiftDirection(box.AxisV, dimension);
                    var lifted = new NarrowphaseMath.Box3
                    {
                        Center = NarrowphaseMath.LiftPoint(box.Center, dimension, inactive),
                        AxisX = axisX,
                        AxisY = axisY,
                        AxisZ = NarrowphaseMath.NormalizeOr(math.cross(axisX, axisY), new float3(0f, 0f, 1f)),
                        // 平面法向半范围为 0：射线在该轴分量为 0，slab 测试直接跳过。
                        Extents = new float3(box.Extents.x, box.Extents.y, 0f),
                    };
                    if (!RayBox(rayOrigin, rayDirection, lifted, planarMaxDistance, out planarDistance, out normal))
                        return false;
                    break;
                }

                case ShapeType.Polygon2D:
                {
                    if (!RayPolygon2D(planar, origin2, direction2 / direction2Length, planarMaxDistance,
                            out planarDistance, out float2 normal2))
                        return false;
                    normal = NarrowphaseMath.LiftDirection(normal2, dimension);
                    break;
                }

                default:
                    return false;
            }

            distance = planarDistance / direction2Length;
            return true;
        }

        /// <summary>射线 vs 球（direction 已归一化，起点在球外）。</summary>
        private static bool RaySphere(
            float3 origin, float3 direction, float3 center, float radius, float maxDistance,
            out float distance)
        {
            distance = 0f;
            float3 offset = origin - center;
            float projected = math.dot(offset, direction);
            float constant = math.dot(offset, offset) - radius * radius;
            float discriminant = projected * projected - constant;
            if (discriminant < 0f) return false;

            float root = math.sqrt(discriminant);
            float near = -projected - root;
            if (near < 0f) near = -projected + root;
            if (near < 0f || near > maxDistance) return false;

            distance = near;
            return true;
        }

        /// <summary>
        /// 射线 vs 半球面：取球面近交点，并要求该点在 <paramref name="outward"/> 正向一侧。
        /// 用于胶囊端帽 —— 端帽球与柱段重合的那半在胶囊内部，不算表面。
        /// </summary>
        private static bool RaySphereCap(
            float3 origin, float3 direction, float3 center, float radius, float3 outward,
            float maxDistance, out float distance)
        {
            distance = 0f;
            float3 offset = origin - center;
            float projected = math.dot(offset, direction);
            float constant = math.dot(offset, offset) - radius * radius;
            float discriminant = projected * projected - constant;
            if (discriminant < 0f) return false;

            float near = -projected - math.sqrt(discriminant);
            if (near < 0f || near > maxDistance) return false;
            if (math.dot(outward, offset + direction * near) < 0f) return false;

            distance = near;
            return true;
        }

        /// <summary>射线 vs 胶囊：柱面（判别式 + 轴向投影约束）与两个端帽取最近解。</summary>
        private static bool RayCapsule(
            float3 origin, float3 direction, float3 start, float3 end, float radius, float maxDistance,
            out float distance)
        {
            distance = 0f;
            float3 axis = end - start;
            float axisLengthSq = math.lengthsq(axis);
            if (axisLengthSq <= Epsilon * Epsilon)
                return RaySphere(origin, direction, start, radius, maxDistance, out distance);

            float3 toOrigin = origin - start;
            float axisAlongRay = math.dot(axis, direction);
            float axisAlongOrigin = math.dot(axis, toOrigin);
            float rayAlongOrigin = math.dot(direction, toOrigin);
            float originAlongOrigin = math.dot(toOrigin, toOrigin);

            // |axis|² 与半径项已通分：a、b、c 为柱面二次方程的系数
            float a = axisLengthSq - axisAlongRay * axisAlongRay;
            float b = axisLengthSq * rayAlongOrigin - axisAlongOrigin * axisAlongRay;
            float c = axisLengthSq * originAlongOrigin
                - axisAlongOrigin * axisAlongOrigin
                - radius * radius * axisLengthSq;

            float best = float.MaxValue;
            bool hit = false;

            if (math.abs(a) > Epsilon)
            {
                float discriminant = b * b - a * c;
                if (discriminant >= 0f)
                {
                    float t = (-b - math.sqrt(discriminant)) / a;
                    float along = axisAlongOrigin + t * axisAlongRay;
                    if (t >= 0f && t <= maxDistance && along >= 0f && along <= axisLengthSq)
                    {
                        best = t;
                        hit = true;
                    }
                }
            }

            float3 axisUnit = axis * math.rsqrt(axisLengthSq);
            if (RaySphereCap(origin, direction, start, radius, -axisUnit, maxDistance, out float capStart)
                && capStart < best)
            {
                best = capStart;
                hit = true;
            }
            if (RaySphereCap(origin, direction, end, radius, axisUnit, maxDistance, out float capEnd)
                && capEnd < best)
            {
                best = capEnd;
                hit = true;
            }

            if (!hit) return false;
            distance = best;
            return true;
        }

        /// <summary>
        /// 射线 vs 有向盒：变换到盒局部空间（盒退化为 AABB）后复用
        /// <see cref="CollisionQueryMath.TryRayAabb"/>，法线再变换回世界空间。
        /// 局部轴正交，方向长度与参数 t 均保持不变。
        /// </summary>
        private static bool RayBox(
            float3 origin, float3 direction, in NarrowphaseMath.Box3 box, float maxDistance,
            out float distance, out float3 normal)
        {
            distance = 0f;
            normal = float3.zero;

            float3 localOrigin = NarrowphaseMath.ToLocal(box, origin);
            float3 localDirection = new(
                math.dot(direction, box.AxisX),
                math.dot(direction, box.AxisY),
                math.dot(direction, box.AxisZ));

            Aabb localBounds = Aabb.FromCenterExtents(float3.zero, box.Extents);
            if (!CollisionQueryMath.TryRayAabb(localOrigin, localDirection, maxDistance,
                    localBounds, out distance, out float3 localNormal))
                return false;

            normal = NarrowphaseMath.NormalizeOr(
                box.AxisX * localNormal.x + box.AxisY * localNormal.y + box.AxisZ * localNormal.z,
                -direction);
            return true;
        }

        /// <summary>
        /// 射线 vs 凸多边形：逐边求交取最近命中。起点已在形状外，
        /// 故最近的有效边交点即入射点；法线取指向射线来向的那一侧（顶点绕序无关）。
        /// </summary>
        private static bool RayPolygon2D(
            in NarrowphaseMath.PlanarShape shape, float2 origin, float2 direction, float maxDistance,
            out float distance, out float2 normal)
        {
            distance = 0f;
            normal = new float2(1f, 0f);

            int start = shape.Collider.Params.VertexStart;
            int count = shape.Collider.Params.VertexCount;
            float bestDistance = float.MaxValue;
            float2 bestNormal = new float2(1f, 0f);
            bool hit = false;

            for (int i = 0; i < count; i++)
            {
                float3 localA = shape.Collider.Params.Center + shape.VertexPool[start + i];
                float3 localB = shape.Collider.Params.Center
                    + shape.VertexPool[start + (i + 1) % count];
                float2 a = NarrowphaseMath.Project(shape.Pose.TransformPoint(localA), shape.Dimension);
                float2 b = NarrowphaseMath.Project(shape.Pose.TransformPoint(localB), shape.Dimension);

                if (!RaySegment2D(origin, direction, a, b, maxDistance, out float t)) continue;
                if (t >= bestDistance) continue;

                float2 edgeDirection = NarrowphaseMath.NormalizeOr2(b - a, new float2(1f, 0f));
                float2 candidate = new float2(-edgeDirection.y, edgeDirection.x);
                if (math.dot(candidate, direction) > 0f) candidate = -candidate;

                bestDistance = t;
                bestNormal = candidate;
                hit = true;
            }

            if (!hit) return false;
            distance = bestDistance;
            normal = bestNormal;
            return true;
        }

        /// <summary>射线 vs 线段（direction 已归一化）。</summary>
        private static bool RaySegment2D(
            float2 origin, float2 direction, float2 a, float2 b, float maxDistance, out float distance)
        {
            distance = 0f;
            float2 edge = b - a;
            float denominator = direction.x * edge.y - direction.y * edge.x;
            if (math.abs(denominator) <= Epsilon) return false;

            float2 toOrigin = a - origin;
            float t = (toOrigin.x * edge.y - toOrigin.y * edge.x) / denominator;
            if (t < 0f || t > maxDistance) return false;

            float s = (toOrigin.x * direction.y - toOrigin.y * direction.x) / denominator;
            if (s < 0f || s > 1f) return false;

            distance = t;
            return true;
        }
    }
}
