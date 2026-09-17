using Unity.Mathematics;

namespace Ember.Collision
{
    /// <summary>
    /// 单个接触点（几何输出，不含求解器状态——冲量存放在独立的约束数组，
    /// 让流形保持「纯几何」语义，便于事件与调试消费）。
    /// </summary>
    public struct ContactPoint
    {
        /// <summary>世界空间接触点。</summary>
        public float3 Position;

        /// <summary>沿法线的分离距离；负值表示穿透深度。</summary>
        public float Separation;

        /// <summary>构造。</summary>
        public ContactPoint(float3 position, float separation)
        {
            Position = position;
            Separation = separation;
        }
    }
}
