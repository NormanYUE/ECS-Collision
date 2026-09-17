using Ember;

namespace Ember.Collision
{
    /// <summary>
    /// 碰撞模块的 World 级全局状态（单例组件）：只存标量与
    /// <see cref="BufferHandle"/>，纯 blittable，<b>无需 Dispose</b>——全部scratch 存储
    /// 都在 World 的 buffer store 里，随 <c>World.Dispose</c> 自动释放。
    ///
    /// 采用与 <c>Ember.Core.SpatialTree</c> 完全相同的资源模型，
    /// 所有操经 <see cref="CollisionWorldView"/> 进行。
    ///
    /// 扩容策略：只在串行侧「调度 Job 之前」按当帧实体数一次性扩容（按 2 的幂取整）。
    /// BufferStore 的增长用「另分配 + 拷贝」实现，会搬移<b>全部</b> range 的地址，
    /// 因此 Job 执行期间严禁任何扩容——这是本模块最重要的不变量。
    /// </summary>
    public struct CollisionWorld : ISingletonComponent
    {
        // ---- 每 Chunk 元数据（长度 = chunkCount）----
        internal BufferHandle ChunkInfos;

        /// <summary>每 Chunk 的静态标志（长度 = chunkCount）。Tag 是 Archetype 级的，
        /// 因此「本 Chunk 是否带 Static」只需每 Chunk 一个字节，无需逐实体同步。</summary>
        internal BufferHandle ChunkStaticFlags;  // byte

        // ---- 稠密 per-body 数组（长度 = bodyCount）----
        internal BufferHandle BodyEntities;   // Entity
        internal BufferHandle BodyBounds;     // Aabb（世界空间，2D 无效轴已撑开）
        internal BufferHandle BodyPoses;      // BodyPose
        internal BufferHandle BodyColliders;  // Collider
        internal BufferHandle BodyFilters;    // CollisionFilter
        internal BufferHandle BodyFlags;      // byte（CollisionBody.Flags 镜像）
        internal BufferHandle BodyContactFlags; // byte（P2 窄相命中镜像）
        internal BufferHandle BodyChunks;     // int（稠密下标 → chunk 下标）

        // ---- 宽相（长度 = bodyCount）----
        internal BufferHandle MortonKeys;         // uint
        internal BufferHandle MortonKeysScratch;  // uint（基数排序双缓冲）
        internal BufferHandle BodyOrder;          // int（按 Morton 排序后的稠密下标）
        internal BufferHandle BodyOrderScratch;   // int（基数排序双缓冲）
        internal BufferHandle PairCounts;        // int（每叶候选 pair 数）
        internal BufferHandle PairOffsets;       // int（前缀和，长度 = bodyCount + 1）

        // ---- 基数排序工作区（长度 = blockCount * 256）----
        internal BufferHandle BucketHistogram;  // int
        internal BufferHandle BucketOffsets;    // int
        internal BufferHandle BucketTotals;     // int（长度 = 256）

        // ---- 归约（长度 = ceil(bodyCount / 4096)）----
        internal BufferHandle BlockBounds;  // Aabb（分块包围盒归约中间量）

        // ---- BVH（长度 = 2 * nextPow2(bodyCount) - 1）----
        internal BufferHandle BvhNodes;  // BvhNode

        // ---- 并行遍历栈（长度 = threadCapacity * stackDepth）----
        internal BufferHandle TraversalStack;  // int

        // ---- 粘滞诊断标志（长度 = DiagnosticSlotCount，按槽位写 1；幂等写无需原子操作）----
        internal BufferHandle DiagnosticFlags;  // int

        // ---- 分块扫描的块基址（长度 = 块数 + 1，末位存总数）----
        internal BufferHandle PairScanBlocks;     // int
        internal BufferHandle ContactScanBlocks;  // int

        // ---- 候选 pair（长度 = pairCapacity）----
        internal BufferHandle CandidatePairs;  // CandidatePair

        // ---- 窄相（长度 = 当帧 pair 数）----
        internal BufferHandle ContactCounts;   // int（每 pair 流形数）
        internal BufferHandle ContactOffsets;  // int（前缀和，长度 = pairCount + 1）

        // ---- 接触流形（长度 = contactCapacity）----
        internal BufferHandle Contacts;  // ContactManifold

        // ---- P3 contact pair 历史 / 事件 ----
        internal BufferHandle PreviousContactPairs;  // ContactPairRecord
        internal BufferHandle CurrentContactPairs;   // ContactPairRecord
        internal BufferHandle ContactPairScratch;    // ContactPairRecord
        internal BufferHandle ContactEvents;         // ContactEvent

        // ---- 凸形状顶点池（长度 = vertexCapacity）----
        internal BufferHandle Vertices;  // float3

        // ---- 诊断计数（长度 = 8，Job 侧的溢出 / 截断计数，串行侧汇总告警）----
        internal BufferHandle Diagnostics;  // int

        /// <summary>稠密 body 数组容量。</summary>
        public int BodyCapacity;

        /// <summary>Chunk 元数据容量。</summary>
        public int ChunkCapacity;

        /// <summary>候选 pair 容量。</summary>
        public int PairCapacity;

        /// <summary>接触流形容量。</summary>
        public int ContactCapacity;

        /// <summary>contact pair 历史缓冲容量。</summary>
        public int ContactPairCapacity;

        /// <summary>接触事件缓冲容量。</summary>
        public int ContactEventCapacity;

        /// <summary>BVH 节点容量。</summary>
        public int NodeCapacity;

        /// <summary>并行遍历栈线程容量。</summary>
        public int ThreadCapacity;

        /// <summary>BVH 叶数组容量（bodyCount 向上取整到 2 的幂）。</summary>
        public int LeafCapacity;

        /// <summary>凸形状顶点池容量。</summary>
        public int VertexPoolCapacity;

        /// <summary>凸形状顶点池已用数量。</summary>
        public int VertexPoolCount;

        /// <summary>当帧稠密 body 数。</summary>
        public int BodyCount;

        /// <summary>当帧 Chunk 数。</summary>
        public int ChunkCount;

        /// <summary>当帧排序分块数（基数排序用）。</summary>
        public int SortBlockCount;

        /// <summary>当帧候选 pair 数。</summary>
        public int CandidatePairCount;

        /// <summary>当帧接触流形数。</summary>
        public int ContactCount;

        /// <summary>当帧真实检测到的流形数，不受 MaxContacts 发布上限影响。</summary>
        public int DetectedContactCount;

        /// <summary>上一帧排序后的 contact pair 数。</summary>
        public int PreviousContactPairCount;

        /// <summary>当帧接触事件数。</summary>
        public int ContactEventCount;

        /// <summary>当帧 BVH 内部节点数。</summary>
        public int InternalNodeCount;

        /// <summary>被容量截断的次数（非 0 表示有 pair/contact 丢失，必须告警）。</summary>
        public int OverflowCount;

        /// <summary>诊断槽：pair 遍历栈溢出次数。</summary>
        public const int DiagPairStackOverflow = 0;

        /// <summary>诊断槽：候选 pair 容量截断数。</summary>
        public const int DiagPairCapacityTruncated = 1;

        /// <summary>诊断槽：接触容量截断数。</summary>
        public const int DiagContactCapacityTruncated = 2;

        /// <summary>排序结果是否落在副数组（由趟数奇偶决定，见 CollisionWorldView）。</summary>
        public byte SortResultInScratch;

        /// <summary>当前帧宽相已完整发布，可供 query 读取。</summary>
        public byte QueryReady;

        /// <summary>诊断槽数量。</summary>
        public const int DiagnosticSlotCount = 8;

        /// <summary>是否已完成 buffer 初始化。</summary>
        public readonly bool IsInitialized => !ChunkInfos.IsNull;
    }
}
