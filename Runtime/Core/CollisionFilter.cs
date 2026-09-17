namespace Ember.Collision
{
    /// <summary>
    /// 碰撞层过滤（32 层位掩码）。两个碰撞体可配对的条件是双向的：
    /// <c>(A.BelongsTo &amp; B.CollidesWith) != 0 &amp;&amp; (B.BelongsTo &amp; A.CollidesWith) != 0</c>。
    /// 双向要求避免单侧配置产生的非对称碰撞。
    /// </summary>
    public struct CollisionFilter : IDataComponent
    {
        /// <summary>本碰撞体所属层（位掩码）。</summary>
        public uint BelongsTo;

        /// <summary>本碰撞体愿意与哪些层碰撞（位掩码）。</summary>
        public uint CollidesWith;

        /// <summary>与所有层互撞的默认值。</summary>
        public static CollisionFilter Default => new() { BelongsTo = 1u, CollidesWith = ~0u };

        /// <summary>构造过滤器。</summary>
        public CollisionFilter(uint belongsTo, uint collidesWith)
        {
            BelongsTo = belongsTo;
            CollidesWith = collidesWith;
        }

        /// <summary>构造单层过滤器：仅与自身层互撞。</summary>
        public static CollisionFilter SingleLayer(int layer)
        {
            uint bit = 1u << layer;
            return new CollisionFilter { BelongsTo = bit, CollidesWith = bit };
        }
    }
}
