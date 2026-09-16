namespace Ember.Collision
{
    /// <summary>
    /// <see cref="ShapeType"/> 纯函数谓词（工具类，静态豁免）。
    /// 全部为编译期可内联的范围比较，供 Burst Job 使用。
    /// </summary>
    public static class ShapeTypeExtensions
    {
        /// <summary>2D 形状区间下界（含）。</summary>
        public const int Dimension2DLower = 1;

        /// <summary>2D 形状区间上界（不含）。</summary>
        public const int Dimension2DUpper = 100;

        /// <summary>3D 形状区间下界（含）。</summary>
        public const int Dimension3DLower = 100;

        /// <summary>形状数量上限（用于 pair 分发表索引）。</summary>
        public const int ShapeCount = 200;

        /// <summary>是否为 2D 形状。</summary>
        public static bool Is2D(this ShapeType type) =>
            (int)type >= Dimension2DLower && (int)type < Dimension2DUpper;

        /// <summary>是否为 3D 形状。</summary>
        public static bool Is3D(this ShapeType type) =>
            (int)type >= Dimension3DLower && (int)type < ShapeCount;

        /// <summary>是否为有效形状（非 None 且在合法区间）。</summary>
        public static bool IsValid(this ShapeType type) =>
            type != ShapeType.None && (int)type > 0 && (int)type < ShapeCount;
    }
}
