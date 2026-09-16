namespace Ember.Collision
{
    /// <summary>
    /// 碰撞维度模式。对齐 <c>Ember.Core.SpatialDimension</c> 的取值语义，
    /// 使碰撞管线与框架空间索引共用同一套 2D / 3D 概念，不引入第二套空间约定。
    /// </summary>
    public enum CollisionDimension
    {
        /// <summary>2D，XY 平面（Z 为无效轴）。</summary>
        XY = 0,

        /// <summary>2D，XZ 平面（Y 为无效轴）。</summary>
        XZ = 1,

        /// <summary>3D，全轴（八叉树语义）。</summary>
        XYZ = 2,
    }
}
