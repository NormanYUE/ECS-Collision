namespace Ember.Collision
{
    /// <summary>
    /// 碰撞体形状类型。2D 与 3D 形状共用同一个 <see cref="Collider"/> 组件（union），
    /// 数值区间刻意分离：1..99 为 2D 形状，100..199 为 3D 形状，
    /// 因此 <see cref="ShapeTypeExtensions.Is2D"/> 是一次范围比较。
    /// </summary>
    public enum ShapeType : byte
    {
        /// <summary>无形状（未初始化 / 已禁用）。</summary>
        None = 0,

        // ---- 2D（1..99）----
        /// <summary>2D 圆。</summary>
        Circle = 1,
        /// <summary>2D 盒（当前 XY/XZ 配置平面内的 OBB）。</summary>
        Box2D = 2,
        /// <summary>2D 胶囊（当前 XY/XZ 配置平面内的圆角线段）。</summary>
        Capsule2D = 3,
        /// <summary>2D 凸多边形（局部顶点存于 CollisionWorld 顶点缓冲）。</summary>
        Polygon2D = 4,

        // ---- 3D（100..199）----
        /// <summary>3D 球。</summary>
        Sphere = 100,
        /// <summary>3D 盒（OBB）。</summary>
        Box = 101,
        /// <summary>3D 胶囊（圆角线段）。</summary>
        Capsule = 102,
        /// <summary>3D 凸多面体（顶点/面存于 CollisionWorld 缓冲；GJK/EPA 求解）。</summary>
        Convex = 103,
    }
}
