using Unity.Mathematics;

namespace Ember.Collision
{
    /// <summary>
    /// 形状参数 union（定长，blittable）。所有形状共用同一块存储，
    /// 由 <see cref="Collider.Type"/> 决定哪些字段有效。
    ///
    /// 之所以用固定 union 而非「每形状一个组件」，是因为：
    /// ① 宽相/窄相只需一次查询、一条代码路径；
    /// ② 单 Job 访问的组件数保持 ≤4，不触发 <c>ChunkJobMeta</c> 的 overflow 路径；
    /// ③ 避免 N 种形状互查时的 Archetype 组合爆炸。
    /// </summary>
    public struct ShapeParams
    {
        /// <summary>形状中心相对实体本地原点的偏移（本地空间；2D 使用当前配置平面）。</summary>
        public float3 Center;

        /// <summary>盒 / 多边形半范围。3D 用 xyz；2D 把 xy 解释为局部 U/V。</summary>
        public float3 Extents;

        /// <summary>球 / 圆 / 胶囊端半径。</summary>
        public float Radius;

        /// <summary>胶囊线段部分的半长。</summary>
        public float HalfHeight;

        /// <summary>胶囊轴。3D：0=X、1=Y、2=Z；2D：0=U/X、1=V。</summary>
        public byte Axis;

        /// <summary>凸形状局部顶点在 CollisionWorld 顶点缓冲中的起始下标。</summary>
        public int VertexStart;

        /// <summary>凸形状顶点数。Polygon2D 至少为 3，按凸包语义处理。</summary>
        public int VertexCount;

        /// <summary>球体参数。</summary>
        public static ShapeParams ForSphere(float radius, float3 center = default) => new()
        {
            Radius = radius,
            Center = center,
        };

        /// <summary>盒体参数（半范围）。</summary>
        public static ShapeParams ForBox(float3 halfExtents, float3 center = default) => new()
        {
            Extents = halfExtents,
            Center = center,
        };

        /// <summary>胶囊参数。3D axis：0=X、1=Y、2=Z；2D axis：0=U、1=V。</summary>
        public static ShapeParams ForCapsule(float radius, float halfHeight, byte axis = 1, float3 center = default) => new()
        {
            Radius = radius,
            HalfHeight = halfHeight,
            Axis = axis,
            Center = center,
        };

        /// <summary>凸形状参数（顶点区间在外部顶点缓冲内）。</summary>
        public static ShapeParams ForConvex(int vertexStart, int vertexCount, float3 center = default) => new()
        {
            VertexStart = vertexStart,
            VertexCount = vertexCount,
            Center = center,
        };

        /// <summary>3D 胶囊轴方向（本地空间单位向量）。2D 平面映射由窄相单独处理。</summary>
        public float3 CapsuleAxis
        {
            get
            {
                switch (Axis)
                {
                    case 0: return new float3(1f, 0f, 0f);
                    case 2: return new float3(0f, 0f, 1f);
                    default: return new float3(0f, 1f, 0f);
                }
            }
        }
    }
}
