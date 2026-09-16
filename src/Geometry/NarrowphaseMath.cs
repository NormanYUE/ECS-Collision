using Unity.Mathematics;

namespace Ember.Collision
{
    /// <summary>窄相纯几何内核。Job 只负责调用、计数与散布。</summary>
    public static unsafe class NarrowphaseMath
    {
        private const float Epsilon = 1e-6f;

        internal struct Sphere3
        {
            public float3 Center;
            public float Radius;
        }

        internal struct Capsule3
        {
            public float3 Start;
            public float3 End;
            public float Radius;
        }

        internal struct Box3
        {
            public float3 Center;
            public float3 AxisX;
            public float3 AxisY;
            public float3 AxisZ;
            public float3 Extents;
        }

        internal struct PlanarShape
        {
            public Collider Collider;
            public BodyPose Pose;
            public CollisionDimension Dimension;
            public float3* VertexPool;
            public int VertexPoolLength;
        }

        internal struct Circle2
        {
            public float2 Center;
            public float Radius;
        }

        internal struct Capsule2
        {
            public float2 Start;
            public float2 End;
            public float Radius;
        }

        internal struct Box2
        {
            public float2 Center;
            public float2 AxisU;
            public float2 AxisV;
            public float2 Extents;
        }

        /// <summary>尝试为两个形状构造一个确定性的接触流形。</summary>
        public static bool TryBuildManifold(
            in Collider a,
            in BodyPose poseA,
            in Collider b,
            in BodyPose poseB,
            CollisionDimension dimension,
            float3* vertexPool,
            int vertexPoolLength,
            out ContactManifold manifold)
        {
            manifold = default;
            if (!a.Type.IsValid() || !b.Type.IsValid() || a.Type.Is2D() != b.Type.Is2D())
                return false;

            if (a.Type.Is3D())
            {
                if (dimension != CollisionDimension.XYZ)
                    return false;

                return TryBuild3D(a, poseA, b, poseB, out manifold);
            }

            return dimension != CollisionDimension.XYZ
                && TryBuild2D(a, poseA, b, poseB, dimension, vertexPool, vertexPoolLength, out manifold);
        }

        /// <summary>
        /// 窄相前的成对过滤：双方均启用且活动、层掩码双向允许，且可选地跳过静态-静态。
        /// </summary>
        public static bool IsPairEligible(
            in CollisionFilter a,
            byte flagsA,
            in CollisionFilter b,
            byte flagsB,
            bool skipStaticPairs)
        {
            byte participationBits = (byte)(CollisionBody.EnabledBit | CollisionBody.ActiveBit);
            if ((flagsA & participationBits) != participationBits
                || (flagsB & participationBits) != participationBits)
                return false;
            if (skipStaticPairs
                && (flagsA & CollisionBody.StaticBit) != 0
                && (flagsB & CollisionBody.StaticBit) != 0)
                return false;

            return (a.CollidesWith & b.BelongsTo) != 0
                && (b.CollidesWith & a.BelongsTo) != 0;
        }

        private static bool TryBuild2D(
            in Collider a,
            in BodyPose poseA,
            in Collider b,
            in BodyPose poseB,
            CollisionDimension dimension,
            float3* vertexPool,
            int vertexPoolLength,
            out ContactManifold manifold)
        {
            var shapeA = new PlanarShape
            {
                Collider = a,
                Pose = poseA,
                Dimension = dimension,
                VertexPool = vertexPool,
                VertexPoolLength = vertexPoolLength,
            };
            var shapeB = new PlanarShape
            {
                Collider = b,
                Pose = poseB,
                Dimension = dimension,
                VertexPool = vertexPool,
                VertexPoolLength = vertexPoolLength,
            };
            if (!IsValidPlanarShape(shapeA) || !IsValidPlanarShape(shapeB))
            {
                manifold = default;
                return false;
            }

            if ((int)a.Type > (int)b.Type)
            {
                bool hit = TryBuild2D(b, poseB, a, poseA, dimension, vertexPool, vertexPoolLength, out manifold);
                if (hit) manifold.Normal = -manifold.Normal;
                return hit;
            }

            float inactiveCoordinate = AverageInactiveCoordinate(shapeA, shapeB);
            int pair = (int)a.Type * ShapeTypeExtensions.ShapeCount + (int)b.Type;
            switch (pair)
            {
                case (int)ShapeType.Circle * ShapeTypeExtensions.ShapeCount + (int)ShapeType.Circle:
                {
                    Circle2 circleA = BuildCircle2(shapeA);
                    Circle2 circleB = BuildCircle2(shapeB);
                    return TryRoundedPoints2D(
                        circleA.Center, circleA.Radius, circleB.Center, circleB.Radius,
                        new float2(1f, 0f), dimension, inactiveCoordinate, out manifold);
                }

                case (int)ShapeType.Circle * ShapeTypeExtensions.ShapeCount + (int)ShapeType.Box2D:
                    return TryCircleBox2D(BuildCircle2(shapeA), BuildBox2(shapeB), dimension, inactiveCoordinate, out manifold);

                case (int)ShapeType.Circle * ShapeTypeExtensions.ShapeCount + (int)ShapeType.Capsule2D:
                {
                    Circle2 circle = BuildCircle2(shapeA);
                    Capsule2 capsule = BuildCapsule2(shapeB);
                    float2 point = ClosestPointOnSegment2D(circle.Center, capsule.Start, capsule.End);
                    return TryRoundedPoints2D(
                        circle.Center, circle.Radius, point, capsule.Radius,
                        capsule.Start + capsule.End - circle.Center * 2f,
                        dimension, inactiveCoordinate, out manifold);
                }

                case (int)ShapeType.Box2D * ShapeTypeExtensions.ShapeCount + (int)ShapeType.Box2D:
                    return TryBoxBox2D(BuildBox2(shapeA), BuildBox2(shapeB), dimension, inactiveCoordinate, out manifold);

                case (int)ShapeType.Box2D * ShapeTypeExtensions.ShapeCount + (int)ShapeType.Capsule2D:
                    return TryBoxCapsule2D(BuildBox2(shapeA), BuildCapsule2(shapeB), dimension, inactiveCoordinate, out manifold);

                case (int)ShapeType.Capsule2D * ShapeTypeExtensions.ShapeCount + (int)ShapeType.Capsule2D:
                {
                    Capsule2 capsuleA = BuildCapsule2(shapeA);
                    Capsule2 capsuleB = BuildCapsule2(shapeB);
                    ClosestPointsOnSegments2D(
                        capsuleA.Start, capsuleA.End, capsuleB.Start, capsuleB.End,
                        out float2 pointA, out float2 pointB);
                    return TryRoundedPoints2D(
                        pointA, capsuleA.Radius, pointB, capsuleB.Radius,
                        (capsuleB.Start + capsuleB.End) - (capsuleA.Start + capsuleA.End),
                        dimension, inactiveCoordinate, out manifold);
                }

                default:
                    return TryGjkManifold2D(shapeA, shapeB, inactiveCoordinate, out manifold);
            }
        }

        internal static bool IsValidPlanarShape(in PlanarShape shape)
        {
            switch (shape.Collider.Type)
            {
                case ShapeType.Circle:
                case ShapeType.Box2D:
                case ShapeType.Capsule2D:
                    return true;

                case ShapeType.Polygon2D:
                {
                    int start = shape.Collider.Params.VertexStart;
                    int count = shape.Collider.Params.VertexCount;
                    return shape.VertexPool != null
                        && count >= 3
                        && start >= 0
                        && start <= shape.VertexPoolLength - count;
                }

                default:
                    return false;
            }
        }

        internal static Circle2 BuildCircle2(in PlanarShape shape) => new()
        {
            Center = Center2(shape),
            Radius = math.abs(shape.Collider.Params.Radius * shape.Pose.Scale),
        };

        internal static Capsule2 BuildCapsule2(in PlanarShape shape)
        {
            GetPlaneAxes(shape, out float2 axisU, out float2 axisV);
            float2 center = Center2(shape);
            float2 axis = shape.Collider.Params.Axis == 0 ? axisU : axisV;
            float halfHeight = math.abs(shape.Collider.Params.HalfHeight * shape.Pose.Scale);
            return new Capsule2
            {
                Start = center - axis * halfHeight,
                End = center + axis * halfHeight,
                Radius = math.abs(shape.Collider.Params.Radius * shape.Pose.Scale),
            };
        }

        internal static Box2 BuildBox2(in PlanarShape shape)
        {
            GetPlaneAxes(shape, out float2 axisU, out float2 axisV);
            return new Box2
            {
                Center = Center2(shape),
                AxisU = axisU,
                AxisV = axisV,
                Extents = math.abs(shape.Collider.Params.Extents.xy * shape.Pose.Scale),
            };
        }

        internal static float2 Center2(in PlanarShape shape) =>
            Project(shape.Pose.TransformPoint(shape.Collider.Params.Center), shape.Dimension);

        internal static void GetPlaneAxes(in PlanarShape shape, out float2 axisU, out float2 axisV)
        {
            float3 localV = shape.Dimension == CollisionDimension.XZ
                ? new float3(0f, 0f, 1f)
                : new float3(0f, 1f, 0f);
            axisU = NormalizeOr2(
                Project(shape.Pose.TransformDirection(new float3(1f, 0f, 0f)), shape.Dimension),
                new float2(1f, 0f));
            float2 rawV = Project(shape.Pose.TransformDirection(localV), shape.Dimension);
            axisV = new float2(-axisU.y, axisU.x);
            if (math.dot(axisV, rawV) < 0f) axisV = -axisV;
        }

        private static float AverageInactiveCoordinate(in PlanarShape a, in PlanarShape b)
        {
            float3 centerA = a.Pose.TransformPoint(a.Collider.Params.Center);
            float3 centerB = b.Pose.TransformPoint(b.Collider.Params.Center);
            return a.Dimension == CollisionDimension.XZ
                ? (centerA.y + centerB.y) * 0.5f
                : (centerA.z + centerB.z) * 0.5f;
        }

        private static bool TryRoundedPoints2D(
            float2 pointA,
            float radiusA,
            float2 pointB,
            float radiusB,
            float2 fallbackDirection,
            CollisionDimension dimension,
            float inactiveCoordinate,
            out ContactManifold manifold)
        {
            float2 delta = pointB - pointA;
            float distanceSq = math.lengthsq(delta);
            float radiusSum = radiusA + radiusB;
            float contactRadius = radiusSum + Epsilon;
            if (distanceSq > contactRadius * contactRadius)
            {
                manifold = default;
                return false;
            }

            float distance = math.sqrt(distanceSq);
            float2 normal = NormalizeOr2(delta, NormalizeOr2(fallbackDirection, new float2(1f, 0f)));
            float2 witnessA = pointA + normal * radiusA;
            float2 witnessB = pointB - normal * radiusB;
            BuildPlanarManifold(
                normal, distance - radiusSum, (witnessA + witnessB) * 0.5f,
                dimension, inactiveCoordinate, out manifold);
            return true;
        }

        private static bool TryCircleBox2D(
            in Circle2 circle,
            in Box2 box,
            CollisionDimension dimension,
            float inactiveCoordinate,
            out ContactManifold manifold)
        {
            float2 localCenter = ToLocal2(box, circle.Center);
            float2 localClosest = math.clamp(localCenter, -box.Extents, box.Extents);
            float2 closest = FromLocal2(box, localClosest);
            float2 delta = closest - circle.Center;
            float distanceSq = math.lengthsq(delta);
            if (distanceSq > circle.Radius * circle.Radius)
            {
                manifold = default;
                return false;
            }

            if (distanceSq > Epsilon * Epsilon)
            {
                float distance = math.sqrt(distanceSq);
                float2 normal = delta / distance;
                float2 witness = circle.Center + normal * circle.Radius;
                BuildPlanarManifold(
                    normal, distance - circle.Radius, (witness + closest) * 0.5f,
                    dimension, inactiveCoordinate, out manifold);
                return true;
            }

            ClosestFace2(box, localCenter, out float2 normalInside, out float2 face, out float faceDistance);
            float2 witnessInside = circle.Center + normalInside * circle.Radius;
            BuildPlanarManifold(
                normalInside, -(circle.Radius + faceDistance), (witnessInside + face) * 0.5f,
                dimension, inactiveCoordinate, out manifold);
            return true;
        }

        private static bool TryBoxCapsule2D(
            in Box2 box,
            in Capsule2 capsule,
            CollisionDimension dimension,
            float inactiveCoordinate,
            out ContactManifold manifold)
        {
            float2 start = ToLocal2(box, capsule.Start);
            float2 end = ToLocal2(box, capsule.End);
            ClosestSegmentAabb2D(start, end, box.Extents, out float2 localCapsule, out float2 localBox, out bool intersects);

            float2 capsulePoint = FromLocal2(box, localCapsule);
            float2 boxPoint = FromLocal2(box, localBox);
            float2 capsuleToBox = boxPoint - capsulePoint;
            float distanceSq = math.lengthsq(capsuleToBox);
            if (!intersects && distanceSq > capsule.Radius * capsule.Radius)
            {
                manifold = default;
                return false;
            }

            if (!intersects && distanceSq > Epsilon * Epsilon)
            {
                float distance = math.sqrt(distanceSq);
                float2 normal = -capsuleToBox / distance;
                float2 capsuleWitness = capsulePoint - normal * capsule.Radius;
                BuildPlanarManifold(
                    normal, distance - capsule.Radius, (boxPoint + capsuleWitness) * 0.5f,
                    dimension, inactiveCoordinate, out manifold);
                return true;
            }

            ClosestFace2(box, localCapsule, out float2 capsuleToFace, out float2 face, out float faceDistance);
            float2 normalFromBox = capsuleToFace;
            float2 insideWitness = capsulePoint + normalFromBox * capsule.Radius;
            BuildPlanarManifold(
                normalFromBox, -(capsule.Radius + faceDistance), (face + insideWitness) * 0.5f,
                dimension, inactiveCoordinate, out manifold);
            return true;
        }

        private static bool TryBoxBox2D(
            in Box2 a,
            in Box2 b,
            CollisionDimension dimension,
            float inactiveCoordinate,
            out ContactManifold manifold)
        {
            float2 centerDelta = b.Center - a.Center;
            float smallestOverlap = 1e30f;
            float2 normal = new float2(1f, 0f);
            if (!TrySatAxis2D(centerDelta, a.AxisU, a.Extents.x, ProjectedRadius2D(b, a.AxisU), ref smallestOverlap, ref normal)
                || !TrySatAxis2D(centerDelta, a.AxisV, a.Extents.y, ProjectedRadius2D(b, a.AxisV), ref smallestOverlap, ref normal)
                || !TrySatAxis2D(centerDelta, b.AxisU, ProjectedRadius2D(a, b.AxisU), b.Extents.x, ref smallestOverlap, ref normal)
                || !TrySatAxis2D(centerDelta, b.AxisV, ProjectedRadius2D(a, b.AxisV), b.Extents.y, ref smallestOverlap, ref normal))
            {
                manifold = default;
                return false;
            }

            float2 witnessA = SupportBox2(a, normal);
            float2 witnessB = SupportBox2(b, -normal);
            BuildPlanarManifold(
                normal, -smallestOverlap, (witnessA + witnessB) * 0.5f,
                dimension, inactiveCoordinate, out manifold);
            return true;
        }

        private static bool TrySatAxis2D(
            float2 centerDelta,
            float2 axis,
            float radiusA,
            float radiusB,
            ref float smallestOverlap,
            ref float2 bestNormal)
        {
            float signedDistance = math.dot(centerDelta, axis);
            float overlap = radiusA + radiusB - math.abs(signedDistance);
            if (overlap < 0f) return false;
            if (overlap < smallestOverlap)
            {
                smallestOverlap = overlap;
                bestNormal = signedDistance >= 0f ? axis : -axis;
            }

            return true;
        }

        private static float ProjectedRadius2D(in Box2 box, float2 axis) =>
            box.Extents.x * math.abs(math.dot(axis, box.AxisU))
            + box.Extents.y * math.abs(math.dot(axis, box.AxisV));

        private static float2 SupportBox2(in Box2 box, float2 direction) =>
            box.Center
            + box.AxisU * (math.dot(direction, box.AxisU) >= 0f ? box.Extents.x : -box.Extents.x)
            + box.AxisV * (math.dot(direction, box.AxisV) >= 0f ? box.Extents.y : -box.Extents.y);

        internal static float2 ToLocal2(in Box2 box, float2 point)
        {
            float2 delta = point - box.Center;
            return new float2(math.dot(delta, box.AxisU), math.dot(delta, box.AxisV));
        }

        internal static float2 FromLocal2(in Box2 box, float2 point) =>
            box.Center + box.AxisU * point.x + box.AxisV * point.y;

        private static void ClosestFace2(
            in Box2 box,
            float2 localPoint,
            out float2 normal,
            out float2 facePoint,
            out float faceDistance)
        {
            float2 distances = box.Extents - math.abs(localPoint);
            bool useU = distances.x <= distances.y;
            float sign = (useU ? localPoint.x : localPoint.y) >= 0f ? 1f : -1f;
            float2 localFace = localPoint;
            if (useU)
            {
                localFace.x = sign * box.Extents.x;
                normal = box.AxisU * sign;
                faceDistance = math.max(0f, distances.x);
            }
            else
            {
                localFace.y = sign * box.Extents.y;
                normal = box.AxisV * sign;
                faceDistance = math.max(0f, distances.y);
            }

            facePoint = FromLocal2(box, localFace);
        }

        private static bool TryGjkManifold2D(
            in PlanarShape a,
            in PlanarShape b,
            float inactiveCoordinate,
            out ContactManifold manifold)
        {
            if (!TryGjk2D(a, b, out float2 point0, out float2 point1, out float2 point2, out int simplexCount))
            {
                manifold = default;
                return false;
            }

            float2 centerDelta = Center2(b) - Center2(a);
            float2 normal;
            float depth;
            if (simplexCount == 3 && TryEpa2D(a, b, point0, point1, point2, out normal, out depth))
            {
                if (math.dot(normal, centerDelta) < 0f) normal = -normal;
            }
            else
            {
                normal = NormalizeOr2(centerDelta, new float2(1f, 0f));
                float2 supportA = Support2D(a, normal);
                float2 supportB = Support2D(b, -normal);
                depth = math.max(0f, math.dot(supportA - supportB, normal));
            }

            float2 witnessA = Support2D(a, normal);
            float2 witnessB = Support2D(b, -normal);
            BuildPlanarManifold(
                normal, -depth, (witnessA + witnessB) * 0.5f,
                a.Dimension, inactiveCoordinate, out manifold);
            return true;
        }

        private static bool TryGjk2D(
            in PlanarShape a,
            in PlanarShape b,
            out float2 point0,
            out float2 point1,
            out float2 point2,
            out int simplexCount)
        {
            float2* simplex = stackalloc float2[3];
            simplexCount = 1;
            float2 direction = Center2(b) - Center2(a);
            direction = NormalizeOr2(direction, new float2(1f, 0f));
            simplex[0] = SupportMinkowski2D(a, b, direction);
            direction = -simplex[0];

            for (int iteration = 0; iteration < 24; iteration++)
            {
                if (math.lengthsq(direction) <= Epsilon * Epsilon)
                {
                    point0 = simplex[0];
                    point1 = simplexCount > 1 ? simplex[1] : default;
                    point2 = simplexCount > 2 ? simplex[2] : default;
                    return true;
                }

                float2 support = SupportMinkowski2D(a, b, direction);
                if (math.dot(support, direction) < -Epsilon)
                {
                    point0 = default;
                    point1 = default;
                    point2 = default;
                    return false;
                }

                simplex[simplexCount++] = support;
                if (UpdateSimplex2D(simplex, ref simplexCount, ref direction))
                {
                    point0 = simplex[0];
                    point1 = simplexCount > 1 ? simplex[1] : default;
                    point2 = simplexCount > 2 ? simplex[2] : default;
                    return true;
                }
            }

            point0 = default;
            point1 = default;
            point2 = default;
            return false;
        }

        private static bool UpdateSimplex2D(float2* simplex, ref int count, ref float2 direction)
        {
            if (count == 2)
            {
                float2 a = simplex[1];
                float2 b = simplex[0];
                float2 ab = b - a;
                float2 ao = -a;
                float lengthSq = math.lengthsq(ab);
                if (lengthSq <= Epsilon * Epsilon)
                {
                    simplex[0] = a;
                    count = 1;
                    direction = ao;
                    return math.lengthsq(direction) <= Epsilon * Epsilon;
                }

                float projection = math.dot(ao, ab);
                if (projection < 0f)
                {
                    simplex[0] = a;
                    count = 1;
                    direction = ao;
                    return math.lengthsq(direction) <= Epsilon * Epsilon;
                }

                if (math.abs(Cross2D(ab, ao)) <= Epsilon && projection <= lengthSq)
                    return true;

                direction = PerpendicularToward(ab, ao);
                return false;
            }

            float2 newest = simplex[2];
            float2 middle = simplex[1];
            float2 oldest = simplex[0];
            float2 aoTriangle = -newest;
            float2 abTriangle = middle - newest;
            float2 acTriangle = oldest - newest;
            float2 outsideAb = PerpendicularAwayFrom(abTriangle, acTriangle);
            if (math.dot(outsideAb, aoTriangle) > 0f)
            {
                simplex[0] = middle;
                simplex[1] = newest;
                count = 2;
                direction = outsideAb;
                return false;
            }

            float2 outsideAc = PerpendicularAwayFrom(acTriangle, abTriangle);
            if (math.dot(outsideAc, aoTriangle) > 0f)
            {
                simplex[0] = oldest;
                simplex[1] = newest;
                count = 2;
                direction = outsideAc;
                return false;
            }

            return true;
        }

        private static bool TryEpa2D(
            in PlanarShape a,
            in PlanarShape b,
            float2 point0,
            float2 point1,
            float2 point2,
            out float2 normal,
            out float depth)
        {
            float2* polytope = stackalloc float2[32];
            polytope[0] = point0;
            polytope[1] = point1;
            polytope[2] = point2;
            if (Cross2D(polytope[1] - polytope[0], polytope[2] - polytope[0]) < 0f)
            {
                float2 swap = polytope[1];
                polytope[1] = polytope[2];
                polytope[2] = swap;
            }

            int count = 3;
            for (int iteration = 0; iteration < 24; iteration++)
            {
                depth = 1e30f;
                normal = new float2(1f, 0f);
                int edge = 0;
                for (int i = 0; i < count; i++)
                {
                    float2 from = polytope[i];
                    float2 to = polytope[(i + 1) % count];
                    float2 edgeVector = to - from;
                    float2 edgeNormal = NormalizeOr2(new float2(edgeVector.y, -edgeVector.x), new float2(1f, 0f));
                    float edgeDistance = math.dot(edgeNormal, from);
                    if (edgeDistance < 0f)
                    {
                        edgeDistance = -edgeDistance;
                        edgeNormal = -edgeNormal;
                    }

                    if (edgeDistance < depth)
                    {
                        depth = edgeDistance;
                        normal = edgeNormal;
                        edge = i;
                    }
                }

                float2 support = SupportMinkowski2D(a, b, normal);
                if (math.dot(support, normal) - depth <= 1e-4f)
                    return true;
                if (count == 32) break;

                int insert = edge + 1;
                for (int i = count; i > insert; i--)
                    polytope[i] = polytope[i - 1];
                polytope[insert] = support;
                count++;
            }

            normal = default;
            depth = 0f;
            return false;
        }

        private static float2 SupportMinkowski2D(in PlanarShape a, in PlanarShape b, float2 direction) =>
            Support2D(a, direction) - Support2D(b, -direction);

        private static float2 Support2D(in PlanarShape shape, float2 direction)
        {
            float2 normalized = NormalizeOr2(direction, new float2(1f, 0f));
            switch (shape.Collider.Type)
            {
                case ShapeType.Circle:
                {
                    Circle2 circle = BuildCircle2(shape);
                    return circle.Center + normalized * circle.Radius;
                }

                case ShapeType.Box2D:
                    return SupportBox2(BuildBox2(shape), normalized);

                case ShapeType.Capsule2D:
                {
                    Capsule2 capsule = BuildCapsule2(shape);
                    float2 endpoint = math.dot(capsule.Start, normalized) >= math.dot(capsule.End, normalized)
                        ? capsule.Start
                        : capsule.End;
                    return endpoint + normalized * capsule.Radius;
                }

                case ShapeType.Polygon2D:
                {
                    float2 best = default;
                    float bestProjection = -1e30f;
                    int start = shape.Collider.Params.VertexStart;
                    int count = shape.Collider.Params.VertexCount;
                    for (int i = 0; i < count; i++)
                    {
                        float3 local = shape.Collider.Params.Center + shape.VertexPool[start + i];
                        float2 candidate = Project(shape.Pose.TransformPoint(local), shape.Dimension);
                        float projection = math.dot(candidate, normalized);
                        if (projection > bestProjection)
                        {
                            bestProjection = projection;
                            best = candidate;
                        }
                    }

                    return best;
                }

                default:
                    return Center2(shape);
            }
        }

        private static void ClosestSegmentAabb2D(
            float2 start,
            float2 end,
            float2 extents,
            out float2 segmentPoint,
            out float2 boxPoint,
            out bool intersects)
        {
            if (TrySegmentAabb2D(start, end, extents, out float enter))
            {
                segmentPoint = math.lerp(start, end, enter);
                boxPoint = segmentPoint;
                intersects = true;
                return;
            }

            float low = 0f;
            float high = 1f;
            for (int i = 0; i < 24; i++)
            {
                float left = (2f * low + high) / 3f;
                float right = (low + 2f * high) / 3f;
                float leftDistance = PointAabbDistanceSq2D(math.lerp(start, end, left), extents);
                float rightDistance = PointAabbDistanceSq2D(math.lerp(start, end, right), extents);
                if (leftDistance <= rightDistance) high = right;
                else low = left;
            }

            float bestT = (low + high) * 0.5f;
            float bestDistance = PointAabbDistanceSq2D(math.lerp(start, end, bestT), extents);
            float startDistance = PointAabbDistanceSq2D(start, extents);
            if (startDistance <= bestDistance + Epsilon * Epsilon)
            {
                bestT = 0f;
                bestDistance = startDistance;
            }

            float endDistance = PointAabbDistanceSq2D(end, extents);
            if (endDistance <= bestDistance + Epsilon * Epsilon)
                bestT = 1f;

            segmentPoint = math.lerp(start, end, bestT);
            boxPoint = math.clamp(segmentPoint, -extents, extents);
            intersects = false;
        }

        private static bool TrySegmentAabb2D(float2 start, float2 end, float2 extents, out float enter)
        {
            float2 direction = end - start;
            float exit = 1f;
            enter = 0f;
            if (!ClipSegmentAxis(ref enter, ref exit, start.x, direction.x, extents.x)
                || !ClipSegmentAxis(ref enter, ref exit, start.y, direction.y, extents.y))
            {
                enter = 0f;
                return false;
            }

            return true;
        }

        internal static float PointAabbDistanceSq2D(float2 point, float2 extents) =>
            math.lengthsq(point - math.clamp(point, -extents, extents));

        internal static float2 ClosestPointOnSegment2D(float2 point, float2 start, float2 end)
        {
            float2 direction = end - start;
            float lengthSq = math.lengthsq(direction);
            if (lengthSq <= Epsilon * Epsilon) return start;
            float t = math.saturate(math.dot(point - start, direction) / lengthSq);
            return start + direction * t;
        }

        internal static void ClosestPointsOnSegments2D(
            float2 startA,
            float2 endA,
            float2 startB,
            float2 endB,
            out float2 pointA,
            out float2 pointB)
        {
            float2 directionA = endA - startA;
            float2 directionB = endB - startB;
            float2 originDelta = startA - startB;
            float lengthA = math.dot(directionA, directionA);
            float lengthB = math.dot(directionB, directionB);
            float s;
            float t;

            if (lengthA <= Epsilon * Epsilon && lengthB <= Epsilon * Epsilon)
            {
                s = 0f;
                t = 0f;
            }
            else if (lengthA <= Epsilon * Epsilon)
            {
                s = 0f;
                t = math.saturate(math.dot(directionB, originDelta) / lengthB);
            }
            else
            {
                float dot = math.dot(directionA, directionB);
                float fromA = math.dot(directionA, originDelta);
                if (lengthB <= Epsilon * Epsilon)
                {
                    t = 0f;
                    s = math.saturate(-fromA / lengthA);
                }
                else
                {
                    float fromB = math.dot(directionB, originDelta);
                    float denominator = lengthA * lengthB - dot * dot;
                    s = denominator > Epsilon * Epsilon
                        ? math.saturate((dot * fromB - fromA * lengthB) / denominator)
                        : 0f;
                    t = (dot * s + fromB) / lengthB;
                    if (t < 0f)
                    {
                        t = 0f;
                        s = math.saturate(-fromA / lengthA);
                    }
                    else if (t > 1f)
                    {
                        t = 1f;
                        s = math.saturate((dot - fromA) / lengthA);
                    }
                }
            }

            pointA = startA + directionA * s;
            pointB = startB + directionB * t;
        }

        private static float Cross2D(float2 a, float2 b) => a.x * b.y - a.y * b.x;

        private static float2 PerpendicularToward(float2 vector, float2 target)
        {
            float2 perpendicular = new float2(-vector.y, vector.x);
            if (math.dot(perpendicular, target) < 0f) perpendicular = -perpendicular;
            return NormalizeOr2(perpendicular, target);
        }

        private static float2 PerpendicularAwayFrom(float2 vector, float2 other)
        {
            float2 perpendicular = new float2(-vector.y, vector.x);
            if (math.dot(perpendicular, other) > 0f) perpendicular = -perpendicular;
            return NormalizeOr2(perpendicular, new float2(1f, 0f));
        }

        internal static float2 NormalizeOr2(float2 value, float2 fallback)
        {
            float lengthSq = math.lengthsq(value);
            return lengthSq > Epsilon * Epsilon ? value * math.rsqrt(lengthSq) : fallback;
        }

        internal static float2 Project(float3 point, CollisionDimension dimension) =>
            dimension == CollisionDimension.XZ ? new float2(point.x, point.z) : new float2(point.x, point.y);

        internal static float3 LiftPoint(float2 point, CollisionDimension dimension, float inactiveCoordinate) =>
            dimension == CollisionDimension.XZ
                ? new float3(point.x, inactiveCoordinate, point.y)
                : new float3(point.x, point.y, inactiveCoordinate);

        internal static float3 LiftDirection(float2 direction, CollisionDimension dimension) =>
            dimension == CollisionDimension.XZ
                ? new float3(direction.x, 0f, direction.y)
                : new float3(direction.x, direction.y, 0f);

        private static void BuildPlanarManifold(
            float2 normal,
            float separation,
            float2 position,
            CollisionDimension dimension,
            float inactiveCoordinate,
            out ContactManifold manifold)
        {
            BuildSingleManifold(
                LiftDirection(normal, dimension),
                separation,
                LiftPoint(position, dimension, inactiveCoordinate),
                out manifold);
        }

        private static bool TryBuild3D(
            in Collider a,
            in BodyPose poseA,
            in Collider b,
            in BodyPose poseB,
            out ContactManifold manifold)
        {
            if ((int)a.Type > (int)b.Type)
            {
                bool hit = TryBuild3D(b, poseB, a, poseA, out manifold);
                if (hit) manifold.Normal = -manifold.Normal;
                return hit;
            }

            int pair = (int)a.Type * ShapeTypeExtensions.ShapeCount + (int)b.Type;
            switch (pair)
            {
                case (int)ShapeType.Sphere * ShapeTypeExtensions.ShapeCount + (int)ShapeType.Sphere:
                    return TrySphereSphere(BuildSphere(a, poseA), BuildSphere(b, poseB), out manifold);

                case (int)ShapeType.Sphere * ShapeTypeExtensions.ShapeCount + (int)ShapeType.Box:
                    return TrySphereBox(BuildSphere(a, poseA), BuildBox(b, poseB), out manifold);

                case (int)ShapeType.Sphere * ShapeTypeExtensions.ShapeCount + (int)ShapeType.Capsule:
                    return TrySphereCapsule(BuildSphere(a, poseA), BuildCapsule(b, poseB), out manifold);

                case (int)ShapeType.Box * ShapeTypeExtensions.ShapeCount + (int)ShapeType.Box:
                    return TryBoxBox(BuildBox(a, poseA), BuildBox(b, poseB), out manifold);

                case (int)ShapeType.Box * ShapeTypeExtensions.ShapeCount + (int)ShapeType.Capsule:
                    return TryBoxCapsule(BuildBox(a, poseA), BuildCapsule(b, poseB), out manifold);

                case (int)ShapeType.Capsule * ShapeTypeExtensions.ShapeCount + (int)ShapeType.Capsule:
                    return TryCapsuleCapsule(BuildCapsule(a, poseA), BuildCapsule(b, poseB), out manifold);

                default:
                    manifold = default;
                    return false;
            }
        }

        internal static Sphere3 BuildSphere(in Collider collider, in BodyPose pose) => new()
        {
            Center = pose.TransformPoint(collider.Params.Center),
            Radius = math.abs(collider.Params.Radius * pose.Scale),
        };

        internal static Capsule3 BuildCapsule(in Collider collider, in BodyPose pose)
        {
            float3 center = pose.TransformPoint(collider.Params.Center);
            float3 axis = NormalizeOr(pose.TransformDirection(collider.Params.CapsuleAxis), new float3(0f, 1f, 0f));
            float halfHeight = math.abs(collider.Params.HalfHeight * pose.Scale);
            return new Capsule3
            {
                Start = center - axis * halfHeight,
                End = center + axis * halfHeight,
                Radius = math.abs(collider.Params.Radius * pose.Scale),
            };
        }

        internal static Box3 BuildBox(in Collider collider, in BodyPose pose) => new()
        {
            Center = pose.TransformPoint(collider.Params.Center),
            AxisX = NormalizeOr(pose.TransformDirection(new float3(1f, 0f, 0f)), new float3(1f, 0f, 0f)),
            AxisY = NormalizeOr(pose.TransformDirection(new float3(0f, 1f, 0f)), new float3(0f, 1f, 0f)),
            AxisZ = NormalizeOr(pose.TransformDirection(new float3(0f, 0f, 1f)), new float3(0f, 0f, 1f)),
            Extents = math.abs(collider.Params.Extents * pose.Scale),
        };

        private static bool TrySphereSphere(in Sphere3 a, in Sphere3 b, out ContactManifold manifold) =>
            TryRoundedPoints(a.Center, a.Radius, b.Center, b.Radius, new float3(1f, 0f, 0f), out manifold);

        private static bool TrySphereCapsule(in Sphere3 sphere, in Capsule3 capsule, out ContactManifold manifold)
        {
            float3 capsulePoint = ClosestPointOnSegment(sphere.Center, capsule.Start, capsule.End);
            return TryRoundedPoints(
                sphere.Center, sphere.Radius, capsulePoint, capsule.Radius,
                capsule.Start + capsule.End - sphere.Center * 2f, out manifold);
        }

        private static bool TryCapsuleCapsule(in Capsule3 a, in Capsule3 b, out ContactManifold manifold)
        {
            ClosestPointsOnSegments(a.Start, a.End, b.Start, b.End, out float3 pointA, out float3 pointB);
            return TryRoundedPoints(
                pointA, a.Radius, pointB, b.Radius,
                (b.Start + b.End) - (a.Start + a.End), out manifold);
        }

        private static bool TryRoundedPoints(
            float3 pointA,
            float radiusA,
            float3 pointB,
            float radiusB,
            float3 fallbackDirection,
            out ContactManifold manifold)
        {
            float3 delta = pointB - pointA;
            float distanceSq = math.lengthsq(delta);
            float radiusSum = radiusA + radiusB;
            float contactRadius = radiusSum + Epsilon;
            if (distanceSq > contactRadius * contactRadius)
            {
                manifold = default;
                return false;
            }

            float distance = math.sqrt(distanceSq);
            float3 normal = NormalizeOr(delta, NormalizeOr(fallbackDirection, new float3(1f, 0f, 0f)));
            float3 witnessA = pointA + normal * radiusA;
            float3 witnessB = pointB - normal * radiusB;
            BuildSingleManifold(normal, distance - radiusSum, (witnessA + witnessB) * 0.5f, out manifold);
            return true;
        }

        private static bool TrySphereBox(in Sphere3 sphere, in Box3 box, out ContactManifold manifold)
        {
            float3 localCenter = ToLocal(box, sphere.Center);
            float3 localClosest = math.clamp(localCenter, -box.Extents, box.Extents);
            float3 closest = FromLocal(box, localClosest);
            float3 delta = closest - sphere.Center;
            float distanceSq = math.lengthsq(delta);
            if (distanceSq > sphere.Radius * sphere.Radius)
            {
                manifold = default;
                return false;
            }

            if (distanceSq > Epsilon * Epsilon)
            {
                float distance = math.sqrt(distanceSq);
                float3 normal = delta / distance;
                float3 witnessA = sphere.Center + normal * sphere.Radius;
                BuildSingleManifold(normal, distance - sphere.Radius, (witnessA + closest) * 0.5f, out manifold);
                return true;
            }

            ClosestFace(box, localCenter, out float3 interiorNormal, out float3 facePoint, out float faceDistance);
            float3 interiorWitness = sphere.Center + interiorNormal * sphere.Radius;
            BuildSingleManifold(
                interiorNormal,
                -(sphere.Radius + faceDistance),
                (interiorWitness + facePoint) * 0.5f,
                out manifold);
            return true;
        }

        private static bool TryBoxCapsule(in Box3 box, in Capsule3 capsule, out ContactManifold manifold)
        {
            float3 start = ToLocal(box, capsule.Start);
            float3 end = ToLocal(box, capsule.End);
            ClosestSegmentAabb(start, end, box.Extents, out float3 localCapsulePoint, out float3 localBoxPoint, out bool intersects);

            float3 capsulePoint = FromLocal(box, localCapsulePoint);
            float3 boxPoint = FromLocal(box, localBoxPoint);
            float3 capsuleToBox = boxPoint - capsulePoint;
            float distanceSq = math.lengthsq(capsuleToBox);
            if (!intersects && distanceSq > capsule.Radius * capsule.Radius)
            {
                manifold = default;
                return false;
            }

            if (!intersects && distanceSq > Epsilon * Epsilon)
            {
                float distance = math.sqrt(distanceSq);
                float3 normal = -capsuleToBox / distance;
                float3 capsuleWitness = capsulePoint - normal * capsule.Radius;
                BuildSingleManifold(normal, distance - capsule.Radius, (boxPoint + capsuleWitness) * 0.5f, out manifold);
                return true;
            }

            ClosestFace(box, localCapsulePoint, out float3 capsuleToFace, out float3 facePoint, out float faceDistance);
            float3 normalFromBox = capsuleToFace;
            float3 capsuleWitnessInside = capsulePoint + normalFromBox * capsule.Radius;
            BuildSingleManifold(
                normalFromBox,
                -(capsule.Radius + faceDistance),
                (facePoint + capsuleWitnessInside) * 0.5f,
                out manifold);
            return true;
        }

        private static bool TryBoxBox(in Box3 a, in Box3 b, out ContactManifold manifold)
        {
            float3 centerDelta = b.Center - a.Center;
            float smallestOverlap = 1e30f;
            float3 bestNormal = new float3(1f, 0f, 0f);

            for (int i = 0; i < 3; i++)
            {
                float3 axis = BoxAxis(a, i);
                float radiusA = VectorComponent(a.Extents, i);
                float radiusB = ProjectedRadius(b, axis);
                if (!TrySatAxis(centerDelta, axis, radiusA, radiusB, ref smallestOverlap, ref bestNormal))
                {
                    manifold = default;
                    return false;
                }
            }

            for (int i = 0; i < 3; i++)
            {
                float3 axis = BoxAxis(b, i);
                float radiusA = ProjectedRadius(a, axis);
                float radiusB = VectorComponent(b.Extents, i);
                if (!TrySatAxis(centerDelta, axis, radiusA, radiusB, ref smallestOverlap, ref bestNormal))
                {
                    manifold = default;
                    return false;
                }
            }

            for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
            {
                float3 cross = math.cross(BoxAxis(a, i), BoxAxis(b, j));
                float lengthSq = math.lengthsq(cross);
                if (lengthSq <= Epsilon * Epsilon) continue;

                float3 axis = cross * math.rsqrt(lengthSq);
                if (!TrySatAxis(
                    centerDelta, axis, ProjectedRadius(a, axis), ProjectedRadius(b, axis),
                    ref smallestOverlap, ref bestNormal))
                {
                    manifold = default;
                    return false;
                }
            }

            float3 witnessA = Support(a, bestNormal);
            float3 witnessB = Support(b, -bestNormal);
            BuildSingleManifold(bestNormal, -smallestOverlap, (witnessA + witnessB) * 0.5f, out manifold);
            return true;
        }

        private static bool TrySatAxis(
            float3 centerDelta,
            float3 axis,
            float radiusA,
            float radiusB,
            ref float smallestOverlap,
            ref float3 bestNormal)
        {
            float signedDistance = math.dot(centerDelta, axis);
            float overlap = radiusA + radiusB - math.abs(signedDistance);
            if (overlap < 0f) return false;

            if (overlap < smallestOverlap)
            {
                smallestOverlap = overlap;
                bestNormal = signedDistance >= 0f ? axis : -axis;
            }

            return true;
        }

        private static float ProjectedRadius(in Box3 box, float3 axis) =>
            box.Extents.x * math.abs(math.dot(axis, box.AxisX))
            + box.Extents.y * math.abs(math.dot(axis, box.AxisY))
            + box.Extents.z * math.abs(math.dot(axis, box.AxisZ));

        private static float3 Support(in Box3 box, float3 direction) =>
            box.Center
            + box.AxisX * (math.dot(direction, box.AxisX) >= 0f ? box.Extents.x : -box.Extents.x)
            + box.AxisY * (math.dot(direction, box.AxisY) >= 0f ? box.Extents.y : -box.Extents.y)
            + box.AxisZ * (math.dot(direction, box.AxisZ) >= 0f ? box.Extents.z : -box.Extents.z);

        private static float3 BoxAxis(in Box3 box, int index)
        {
            switch (index)
            {
                case 0: return box.AxisX;
                case 1: return box.AxisY;
                default: return box.AxisZ;
            }
        }

        private static float VectorComponent(float3 value, int index)
        {
            switch (index)
            {
                case 0: return value.x;
                case 1: return value.y;
                default: return value.z;
            }
        }

        internal static float3 ToLocal(in Box3 box, float3 point)
        {
            float3 delta = point - box.Center;
            return new float3(
                math.dot(delta, box.AxisX),
                math.dot(delta, box.AxisY),
                math.dot(delta, box.AxisZ));
        }

        internal static float3 FromLocal(in Box3 box, float3 point) =>
            box.Center + box.AxisX * point.x + box.AxisY * point.y + box.AxisZ * point.z;

        private static void ClosestFace(
            in Box3 box,
            float3 localPoint,
            out float3 normal,
            out float3 facePoint,
            out float faceDistance)
        {
            float3 distances = box.Extents - math.abs(localPoint);
            int axis = distances.x <= distances.y && distances.x <= distances.z
                ? 0
                : distances.y <= distances.z ? 1 : 2;
            float sign = VectorComponent(localPoint, axis) >= 0f ? 1f : -1f;
            float3 localFace = localPoint;
            faceDistance = math.max(0f, VectorComponent(distances, axis));

            switch (axis)
            {
                case 0:
                    localFace.x = sign * box.Extents.x;
                    normal = box.AxisX * sign;
                    break;
                case 1:
                    localFace.y = sign * box.Extents.y;
                    normal = box.AxisY * sign;
                    break;
                default:
                    localFace.z = sign * box.Extents.z;
                    normal = box.AxisZ * sign;
                    break;
            }

            facePoint = FromLocal(box, localFace);
        }

        internal static float3 ClosestPointOnSegment(float3 point, float3 start, float3 end)
        {
            float3 direction = end - start;
            float lengthSq = math.lengthsq(direction);
            if (lengthSq <= Epsilon * Epsilon) return start;
            float t = math.saturate(math.dot(point - start, direction) / lengthSq);
            return start + direction * t;
        }

        internal static void ClosestPointsOnSegments(
            float3 startA,
            float3 endA,
            float3 startB,
            float3 endB,
            out float3 pointA,
            out float3 pointB)
        {
            float3 directionA = endA - startA;
            float3 directionB = endB - startB;
            float3 originDelta = startA - startB;
            float lengthA = math.dot(directionA, directionA);
            float lengthB = math.dot(directionB, directionB);
            float s;
            float t;

            if (lengthA <= Epsilon * Epsilon && lengthB <= Epsilon * Epsilon)
            {
                s = 0f;
                t = 0f;
            }
            else if (lengthA <= Epsilon * Epsilon)
            {
                s = 0f;
                t = math.saturate(math.dot(directionB, originDelta) / lengthB);
            }
            else
            {
                float dot = math.dot(directionA, directionB);
                float fromA = math.dot(directionA, originDelta);
                if (lengthB <= Epsilon * Epsilon)
                {
                    t = 0f;
                    s = math.saturate(-fromA / lengthA);
                }
                else
                {
                    float fromB = math.dot(directionB, originDelta);
                    float denominator = lengthA * lengthB - dot * dot;
                    s = denominator > Epsilon * Epsilon
                        ? math.saturate((dot * fromB - fromA * lengthB) / denominator)
                        : 0f;
                    t = (dot * s + fromB) / lengthB;

                    if (t < 0f)
                    {
                        t = 0f;
                        s = math.saturate(-fromA / lengthA);
                    }
                    else if (t > 1f)
                    {
                        t = 1f;
                        s = math.saturate((dot - fromA) / lengthA);
                    }
                }
            }

            pointA = startA + directionA * s;
            pointB = startB + directionB * t;
        }

        private static void ClosestSegmentAabb(
            float3 start,
            float3 end,
            float3 extents,
            out float3 segmentPoint,
            out float3 boxPoint,
            out bool intersects)
        {
            if (TrySegmentAabb(start, end, extents, out float enter))
            {
                segmentPoint = math.lerp(start, end, enter);
                boxPoint = segmentPoint;
                intersects = true;
                return;
            }

            float low = 0f;
            float high = 1f;
            for (int i = 0; i < 24; i++)
            {
                float left = (2f * low + high) / 3f;
                float right = (low + 2f * high) / 3f;
                float leftDistance = PointAabbDistanceSq(math.lerp(start, end, left), extents);
                float rightDistance = PointAabbDistanceSq(math.lerp(start, end, right), extents);
                if (leftDistance <= rightDistance) high = right;
                else low = left;
            }

            float bestT = (low + high) * 0.5f;
            float bestDistance = PointAabbDistanceSq(math.lerp(start, end, bestT), extents);
            float startDistance = PointAabbDistanceSq(start, extents);
            if (startDistance <= bestDistance + Epsilon * Epsilon)
            {
                bestT = 0f;
                bestDistance = startDistance;
            }

            float endDistance = PointAabbDistanceSq(end, extents);
            if (endDistance <= bestDistance + Epsilon * Epsilon)
                bestT = 1f;

            segmentPoint = math.lerp(start, end, bestT);
            boxPoint = math.clamp(segmentPoint, -extents, extents);
            intersects = false;
        }

        private static bool TrySegmentAabb(float3 start, float3 end, float3 extents, out float enter)
        {
            float3 direction = end - start;
            float exit = 1f;
            enter = 0f;
            if (!ClipSegmentAxis(ref enter, ref exit, start.x, direction.x, extents.x)
                || !ClipSegmentAxis(ref enter, ref exit, start.y, direction.y, extents.y)
                || !ClipSegmentAxis(ref enter, ref exit, start.z, direction.z, extents.z))
            {
                enter = 0f;
                return false;
            }

            return true;
        }

        private static bool ClipSegmentAxis(ref float enter, ref float exit, float start, float direction, float extent)
        {
            if (math.abs(direction) <= Epsilon)
                return start >= -extent && start <= extent;

            float inverse = 1f / direction;
            float near = (-extent - start) * inverse;
            float far = (extent - start) * inverse;
            if (near > far)
            {
                float swap = near;
                near = far;
                far = swap;
            }

            enter = math.max(enter, near);
            exit = math.min(exit, far);
            return enter <= exit;
        }

        internal static float PointAabbDistanceSq(float3 point, float3 extents) =>
            math.lengthsq(point - math.clamp(point, -extents, extents));

        internal static float3 NormalizeOr(float3 value, float3 fallback)
        {
            float lengthSq = math.lengthsq(value);
            return lengthSq > Epsilon * Epsilon ? value * math.rsqrt(lengthSq) : fallback;
        }

        private static void BuildSingleManifold(
            float3 normal,
            float separation,
            float3 position,
            out ContactManifold manifold)
        {
            manifold = new ContactManifold
            {
                Normal = normal,
                Count = 1,
                P0 = new ContactPoint(position, math.min(0f, separation)),
            };
        }

    }
}
