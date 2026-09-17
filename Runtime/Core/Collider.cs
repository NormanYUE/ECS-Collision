using Unity.Mathematics;

namespace Ember.Collision
{
    /// <summary>
    /// 碰撞体（2D / 3D 统一）。形状由 <see cref="Type"/> 决定，参数见 <see cref="Params"/>。
    /// 本地空间定义，世界空间由 <c>LocalToWorld</c> 变换得到。
    ///
    /// 约定：形状的本地包围盒由 <c>ShapeBoundsMath</c> 计算，写入
    /// <c>Ember.Core.BoundingVolume</c>，从而直接接入框架既有的
    /// <c>WorldBoundsSystem</c>（视锥剔除 + 空间索引共用同一条链路）。
    /// </summary>
    public struct Collider : IDataComponent
    {
        /// <summary>形状类型。</summary>
        public ShapeType Type;

        /// <summary>形状参数。</summary>
        public ShapeParams Params;

        /// <summary>构造碰撞体。</summary>
        public Collider(ShapeType type, in ShapeParams parameters)
        {
            Type = type;
            Params = parameters;
        }

        /// <summary>球体。</summary>
        public static Collider Sphere(float radius, float3 center = default) =>
            new(ShapeType.Sphere, ShapeParams.ForSphere(radius, center));

        /// <summary>3D 盒（半范围）。</summary>
        public static Collider Box(float3 halfExtents, float3 center = default) =>
            new(ShapeType.Box, ShapeParams.ForBox(halfExtents, center));

        /// <summary>3D 胶囊。axis：0=X, 1=Y, 2=Z。</summary>
        public static Collider Capsule(float radius, float halfHeight, byte axis = 1, float3 center = default) =>
            new(ShapeType.Capsule, ShapeParams.ForCapsule(radius, halfHeight, axis, center));

        /// <summary>2D 圆。</summary>
        public static Collider Circle(float radius, float3 center = default) =>
            new(ShapeType.Circle, ShapeParams.ForSphere(radius, center));

        /// <summary>2D 盒（局部 U/V 半范围；V 映射到 XY 的 Y 或 XZ 的 Z）。</summary>
        public static Collider Box2D(float2 halfExtents, float3 center = default) =>
            new(ShapeType.Box2D, ShapeParams.ForBox(new float3(halfExtents, 0f), center));

        /// <summary>2D 胶囊。axis：0=U/X，1=V（XY 为 Y，XZ 为 Z）。</summary>
        public static Collider Capsule2D(float radius, float halfHeight, byte axis = 1, float3 center = default) =>
            new(ShapeType.Capsule2D, ShapeParams.ForCapsule(radius, halfHeight, axis, center));

        /// <summary>2D 凸多边形。顶点为选定平面中的局部坐标，应先追加到 <c>CollisionWorld</c> 顶点池。</summary>
        public static Collider Polygon2D(int vertexStart, int vertexCount, float3 center = default) =>
            new(ShapeType.Polygon2D, ShapeParams.ForConvex(vertexStart, vertexCount, center));
    }
}
