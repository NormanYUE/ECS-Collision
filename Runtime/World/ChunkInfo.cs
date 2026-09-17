namespace Ember.Collision
{
    /// <summary>
    /// 单个 Chunk 的列指针表项（每帧重建）。
    ///
    /// 存在理由：<c>Chunk</c> 是托管类、<c>GetEntityPtr</c> / <c>GetBufferPtr</c> 均为 internal，
    /// Burst Job 无法调用托管访问器。因此每帧在串行侧用 public 的
    /// <c>Chunk.GetColumn&lt;T&gt;().UnsafePtr</c> 采集列指针，Job 侧只做指针算术。
    /// 这与框架自身 <c>ChunkJobMeta</c> 的机制一致（同样是 <c>long</c> → <c>byte*</c>）。
    ///
    /// <see cref="Base"/> 是「稠密 body 下标」的基址：chunk 顺序 + 行序 决定稠密下标，
    /// 因此映射是确定性的（同输入必得同序），且回写位置可由 <see cref="Base"/> 与行号直接算出。
    /// </summary>
    public struct ChunkInfo
    {
        /// <summary>Collider 列基址。</summary>
        public long ColliderPtr;

        /// <summary>CollisionBody 列基址。</summary>
        public long BodyPtr;

        /// <summary>CollisionFilter 列基址。</summary>
        public long FilterPtr;

        /// <summary>LocalToWorld 列基址。</summary>
        public long LocalToWorldPtr;

        /// <summary>BoundingVolume 列基址（本地 AABB）。由碰撞的 GatherJob 写入，
        /// 从而接入框架既有的 WorldBoundsSystem → 视锥剔除 / 空间索引链路。</summary>
        public long BoundsPtr;

        /// <summary>CollisionState 列基址（窄相写回接触边沿）。</summary>
        public long StatePtr;

        /// <summary>本 Chunk 的行数。</summary>
        public int Count;

        /// <summary>本 Chunk 首行对应的稠密 body 下标。</summary>
        public int Base;
    }
}
