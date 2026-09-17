using Unity.Mathematics;

namespace Ember.Collision
{
    /// <summary>当前帧碰撞 LBVH 的 AABB 射线命中。</summary>
    public struct CollisionRaycastHit
    {
        public Entity Entity;
        public float Distance;
        public float3 Position;
        public float3 Normal;
    }
}
