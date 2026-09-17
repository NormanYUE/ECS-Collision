using Unity.Mathematics;

namespace Ember.Collision
{
    /// <summary>当前帧 broad query 共用的纯几何内核。</summary>
    public static class CollisionQueryMath
    {
        private const float Epsilon = 1e-6f;

        /// <summary>射线与闭区间 AABB 的最近命中；方向会归一化，距离以世界单位返回。</summary>
        public static bool TryRayAabb(
            float3 origin,
            float3 direction,
            float maxDistance,
            in Aabb bounds,
            out float distance,
            out float3 normal)
        {
            distance = 0f;
            normal = float3.zero;
            if (maxDistance < 0f || bounds.IsEmpty) return false;

            float directionLengthSq = math.lengthsq(direction);
            if (directionLengthSq <= Epsilon * Epsilon) return false;
            float3 ray = direction * math.rsqrt(directionLengthSq);
            float enter = 0f;
            float exit = maxDistance;
            normal = -ray;

            if (!ClipAxis(origin.x, ray.x, bounds.Min.x, bounds.Max.x, new float3(-1f, 0f, 0f), new float3(1f, 0f, 0f), ref enter, ref exit, ref normal)
                || !ClipAxis(origin.y, ray.y, bounds.Min.y, bounds.Max.y, new float3(0f, -1f, 0f), new float3(0f, 1f, 0f), ref enter, ref exit, ref normal)
                || !ClipAxis(origin.z, ray.z, bounds.Min.z, bounds.Max.z, new float3(0f, 0f, -1f), new float3(0f, 0f, 1f), ref enter, ref exit, ref normal))
                return false;

            distance = enter;
            return true;
        }

        private static bool ClipAxis(
            float origin,
            float direction,
            float minimum,
            float maximum,
            float3 minimumNormal,
            float3 maximumNormal,
            ref float enter,
            ref float exit,
            ref float3 enterNormal)
        {
            if (math.abs(direction) <= Epsilon)
                return origin >= minimum && origin <= maximum;

            float inverse = 1f / direction;
            float near = (minimum - origin) * inverse;
            float far = (maximum - origin) * inverse;
            float3 nearNormal = minimumNormal;
            if (near > far)
            {
                float swap = near;
                near = far;
                far = swap;
                nearNormal = maximumNormal;
            }

            if (near > enter)
            {
                enter = near;
                enterNormal = nearNormal;
            }

            exit = math.min(exit, far);
            return enter <= exit;
        }
    }
}
