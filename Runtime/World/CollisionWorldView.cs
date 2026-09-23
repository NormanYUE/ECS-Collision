using System;
using System.Runtime.CompilerServices;
using Ember.Core;
using Unity.Collections;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;

namespace Ember.Collision
{
    /// <summary>
    /// 碰撞世界操作视图（轻量 struct：World + 单例实体）。
    /// 全部算法经此进行：标量经组件 ref 读写，scratch 经 World 托管 buffer 访问。
    ///
    /// <b>容量不变量</b>：<see cref="EnsureCapacity"/> 必须在任何 Job 调度之前调用完毕。
    /// BufferStore 的增长通过「另分配 + 拷贝」实现，会搬移所有 range 的地址，
    /// 因此扩容后必须重新获取视图；Job 执行期间严禁扩容。
    /// </summary>
    public struct CollisionWorldView
    {
        /// <summary>基数排序每个分块的元素数。</summary>
        public const int SortBlockSize = 2048;

        /// <summary>并行遍历栈每线程深度。</summary>
        public const int TraversalStackDepth = 512;

        /// <summary>包围盒归约分块大小。</summary>
        public const int BoundsBlockSize = 4096;

        /// <summary>前缀和分块大小。</summary>
        public const int ScanBlockSize = 4096;

        /// <summary>BVH 逐层并行合并的宽度下限（低于此宽度的层交给串行收尾）。</summary>
        public const int BvhParallelWidthThreshold = 64;

        private readonly World m_World;
        private readonly Entity m_Owner;

        internal CollisionWorldView(World world, Entity owner)
        {
            m_World = world;
            m_Owner = owner;
        }

        /// <summary>只读快照（句柄 + 计数）。读取路径专用，避免 readonly 成员的隐式拷贝告警。</summary>
        private readonly CollisionWorld State => m_World.GetComponent<CollisionWorld>(m_Owner);

        /// <summary>可变引用。写入 / 扩容路径专用。</summary>
        private ref CollisionWorld MutableState => ref m_World.GetComponent<CollisionWorld>(m_Owner);

        /// <summary>当帧稠密 body 数。</summary>
        public readonly int BodyCount => m_World.GetComponent<CollisionWorld>(m_Owner).BodyCount;

        /// <summary>当帧 Chunk 数（= <c>ChunkInfoPtr</c> 可取的元素数）。</summary>
        public readonly int ChunkCount => State.ChunkCount;

        /// <summary>接触事件 buffer 容量。</summary>
        public readonly int ContactEventCapacity => State.ContactEventCapacity;

        /// <summary>当帧候选 pair 数。</summary>
        public readonly int CandidatePairCount => m_World.GetComponent<CollisionWorld>(m_Owner).CandidatePairCount;

        /// <summary>BVH 叶数组容量（bodyCount 补足到 2 的幂）。</summary>
        public readonly int LeafCapacity => State.LeafCapacity;

        /// <summary>当帧接触流形数。</summary>
        public readonly int ContactCount => m_World.GetComponent<CollisionWorld>(m_Owner).ContactCount;

        /// <summary>容量溢出次数（&gt; 0 表示当帧丢失了 pair / contact）。</summary>
        public readonly int OverflowCount => m_World.GetComponent<CollisionWorld>(m_Owner).OverflowCount;

        // ---------------------------------------------------------------------
        // 公开 body 快照（N2，供运行时烘焙等消费方读取）
        // ---------------------------------------------------------------------

        /// <summary>
        /// 当帧稠密 body 位姿的只读视图。<b>仅在 <see cref="IsQueryReady"/> 后有效</b>；
        /// 只读，不可写回、不可缓存跨帧（下一帧重填 / 扩容后视图即失效）。
        /// 零分配 —— 直接包装 World 托管 buffer 的裸内存。
        /// </summary>
        public readonly long BodyPosesPtr
        {
            get
            {
                RequireQueryReady(nameof(BodyPosesPtr));
                return NativePointer<BodyPose>(State.BodyPoses, State.BodyCount);
            }
        }

        /// <summary>当帧稠密 body 碰撞体的只读视图。有效性约束同 <see cref="BodyPoses"/>。</summary>
        public readonly long BodyCollidersPtr
        {
            get
            {
                RequireQueryReady(nameof(BodyCollidersPtr));
                return NativePointer<Collider>(State.BodyColliders, State.BodyCount);
            }
        }

        /// <summary>当帧稠密 body 过滤层的只读视图。有效性约束同 <see cref="BodyPoses"/>。</summary>
        public readonly long BodyFiltersPtr
        {
            get
            {
                RequireQueryReady(nameof(BodyFiltersPtr));
                return NativePointer<CollisionFilter>(State.BodyFilters, State.BodyCount);
            }
        }

        /// <summary>当帧稠密 body 标志（含 Static 位）的只读视图。有效性约束同 <see cref="BodyPoses"/>。</summary>
        public readonly long BodyFlagsPtr
        {
            get
            {
                RequireQueryReady(nameof(BodyFlagsPtr));
                return NativePointer<byte>(State.BodyFlags, State.BodyCount);
            }
        }

        /// <summary>
        /// 当帧稠密 body 的<b>接触标志</b>（每体 1 字节，非 0 = 本帧至少参与一个真实接触）。
        /// 由窄相的 flag mark 阶段写入，与实体上的 <c>CollisionState.HasContact</c> 同源，
        /// 且**不受 <c>CollisionConfig.MaxContacts</c> 截断影响**（截断只影响已发布的流形）。
        /// 长度 = <see cref="BodyCount"/>。有效性约束同 <see cref="BodyPoses"/>。
        ///
        /// 供调试可视化 / 运行时烘焙使用：可以直接据此把「正在碰撞」的碰撞体高亮出来。
        /// </summary>
        public readonly long BodyContactFlagsPtr
        {
            get
            {
                RequireQueryReady(nameof(BodyContactFlagsPtr));
                return NativePointer<byte>(State.BodyContactFlags, State.BodyCount);
            }
        }

        /// <summary>凸形状顶点池的只读视图（Polygon2D 顶点存储，长度 <see cref="VertexPoolCount"/>）。有效性约束同 <see cref="BodyPoses"/>。</summary>
        public readonly long VertexPoolPtr
        {
            get
            {
                RequireQueryReady(nameof(VertexPoolPtr));
                return NativePointer<float3>(State.Vertices, State.VertexPoolCount);
            }
        }

        private readonly void RequireQueryReady(string accessorName)
        {
            if (State.QueryReady == 0)
                throw new System.InvalidOperationException(
                    $"CollisionWorldView.{accessorName}: body snapshot is only valid after the broadphase has published (IsQueryReady). Read-only; do not cache across frames.");
        }

        // ---------------------------------------------------------------------
        // 初始化与扩容
        // ---------------------------------------------------------------------

        /// <summary>
        /// 首次使用时创建全部 scratch buffer（重复调用为空操作）。
        /// 一律用 <c>CreateSizedBuffer</c> 而不是 <c>CreateBuffer</c>：后者的逻辑长度是 0，
        /// 建出来必须再 ResizeBuffer 才能按下标用；本类的访问器是按长度取指针的，漏一次就退化成空指针。
        /// </summary>
        internal void EnsureInitialized()
        {
            ref var state = ref MutableState;
            if (state.IsInitialized) return;

            state.ChunkInfos = m_World.CreateSizedBuffer<ChunkInfo>(8);
            state.ChunkStaticFlags = m_World.CreateSizedBuffer<byte>(8);
            state.BodyEntities = m_World.CreateSizedBuffer<Entity>(16);
            state.BodyBounds = m_World.CreateSizedBuffer<Aabb>(16);
            state.BodyPoses = m_World.CreateSizedBuffer<BodyPose>(16);
            state.BodyColliders = m_World.CreateSizedBuffer<Collider>(16);
            state.BodyFilters = m_World.CreateSizedBuffer<CollisionFilter>(16);
            state.BodyFlags = m_World.CreateSizedBuffer<byte>(16);
            state.BodyContactFlags = m_World.CreateSizedBuffer<byte>(16);
            state.BodyChunks = m_World.CreateSizedBuffer<int>(16);
            state.MortonKeys = m_World.CreateSizedBuffer<uint>(16);
            state.MortonKeysScratch = m_World.CreateSizedBuffer<uint>(16);
            state.BodyOrder = m_World.CreateSizedBuffer<int>(16);
            state.BodyOrderScratch = m_World.CreateSizedBuffer<int>(16);
            state.PairCounts = m_World.CreateSizedBuffer<int>(16);
            state.PairOffsets = m_World.CreateSizedBuffer<int>(17);
            state.BucketHistogram = m_World.CreateSizedBuffer<int>(256);
            state.BucketOffsets = m_World.CreateSizedBuffer<int>(256);
            state.BucketTotals = m_World.CreateSizedBuffer<int>(256);
            state.BlockBounds = m_World.CreateSizedBuffer<Aabb>(4);
            state.BvhNodes = m_World.CreateSizedBuffer<BvhNode>(32);
            state.TraversalStack = m_World.CreateSizedBuffer<int>(64);
            state.DiagnosticFlags = m_World.CreateSizedBuffer<int>(CollisionWorld.DiagnosticSlotCount);
            state.PairScanBlocks = m_World.CreateSizedBuffer<int>(4);
            state.ContactScanBlocks = m_World.CreateSizedBuffer<int>(4);
            state.CandidatePairs = m_World.CreateSizedBuffer<CandidatePair>(16);
            state.ContactCounts = m_World.CreateSizedBuffer<int>(16);
            state.ContactOffsets = m_World.CreateSizedBuffer<int>(17);
            state.Contacts = m_World.CreateSizedBuffer<ContactManifold>(16);
            state.PreviousContactPairs = m_World.CreateSizedBuffer<ContactPairRecord>(16);
            state.CurrentContactPairs = m_World.CreateSizedBuffer<ContactPairRecord>(16);
            state.ContactPairScratch = m_World.CreateSizedBuffer<ContactPairRecord>(16);
            state.ContactEvents = m_World.CreateSizedBuffer<ContactEvent>(32);
            state.Vertices = m_World.CreateSizedBuffer<float3>(64);
            state.Diagnostics = m_World.CreateSizedBuffer<int>(CollisionWorld.DiagnosticSlotCount);
        }

        /// <summary>
        /// 按当帧规模一次性扩容全部 scratch。必须在调度任何 Job 之前调用；
        /// 内部按 2 的幂取整，使增长次数为 O(log n)。
        /// </summary>
        public void EnsureCapacity(
            int bodyCount,
            int chunkCount,
            int candidatePairCapacity,
            int contactCapacity)
        {
            EnsureInitialized();

            ref var state = ref MutableState;
            int threadCapacity = math.max(1, JobsUtility.MaxJobThreadCount);
            int leafCapacity = math.max(2, NextPowerOfTwo(bodyCount));
            int nodeCount = 2 * leafCapacity - 1;
            int blockCount = math.max(1, (bodyCount + SortBlockSize - 1) / SortBlockSize);
            int bucketCount = blockCount * 256;
            int boundsBlocks = math.max(1, (bodyCount + BoundsBlockSize - 1) / BoundsBlockSize);

            Grow<Entity>(ref state.BodyEntities, bodyCount);
            Grow<Aabb>(ref state.BodyBounds, bodyCount);
            Grow<BodyPose>(ref state.BodyPoses, bodyCount);
            Grow<Collider>(ref state.BodyColliders, bodyCount);
            Grow<CollisionFilter>(ref state.BodyFilters, bodyCount);
            Grow<byte>(ref state.BodyFlags, bodyCount);
            Grow<byte>(ref state.BodyContactFlags, bodyCount);
            Grow<int>(ref state.BodyChunks, bodyCount);
            Grow<uint>(ref state.MortonKeys, bodyCount);
            Grow<uint>(ref state.MortonKeysScratch, bodyCount);
            Grow<int>(ref state.BodyOrder, bodyCount);
            Grow<int>(ref state.BodyOrderScratch, bodyCount);
            Grow<int>(ref state.PairCounts, bodyCount);
            Grow<int>(ref state.PairOffsets, bodyCount + 1);
            Grow<int>(ref state.BucketHistogram, bucketCount);
            Grow<int>(ref state.BucketOffsets, bucketCount);
            Grow<int>(ref state.BucketTotals, RadixSort32.BucketCount);
            Grow<Aabb>(ref state.BlockBounds, boundsBlocks);
            Grow<BvhNode>(ref state.BvhNodes, nodeCount);
            Grow<int>(ref state.TraversalStack, threadCapacity * TraversalStackDepth);
            Grow<int>(ref state.PairScanBlocks, BlockScan.BlockCount(bodyCount, ScanBlockSize) + 1);
            // ContactCounts / ContactOffsets 的长度必须跟着 CandidatePairs 的**实际**长度走，
            // 不能跟着这里请求的容量走：Grow 按 2 的幂取整，实际的候选 pair 容量
            // （= CollisionWorldView.PairCapacity）可能大于本次请求值，而访问器是按
            // PairCapacity / PairCapacity + 1 要长度的。
            Grow<CandidatePair>(ref state.CandidatePairs, candidatePairCapacity);
            int grownPairCapacity = m_World.GetBufferLength<CandidatePair>(state.CandidatePairs);
            Grow<int>(ref state.ContactCounts, grownPairCapacity);
            Grow<int>(ref state.ContactOffsets, grownPairCapacity + 1);
            Grow<ContactManifold>(ref state.Contacts, contactCapacity);
            Grow<ContactPairRecord>(ref state.PreviousContactPairs, contactCapacity);
            Grow<ContactPairRecord>(ref state.CurrentContactPairs, contactCapacity);
            Grow<ContactPairRecord>(ref state.ContactPairScratch, contactCapacity);
            Grow<ContactEvent>(ref state.ContactEvents, contactCapacity * 2);
            Grow<int>(ref state.Diagnostics, CollisionWorld.DiagnosticSlotCount);

            // DiagnosticFlags 也必须显式设长度：CreateBuffer(capacity) 只设容量，
            // 逻辑长度是 0，不 Growth 就永远是 0（本文件里唯一一个此前漏掉的）。
            Grow<int>(ref state.DiagnosticFlags, CollisionWorld.DiagnosticSlotCount);

            // 全部扩容完成后才刷新容量字段，避免中途状态被误用。
            state.BodyCapacity = m_World.GetBufferLength<Entity>(state.BodyEntities);
            state.LeafCapacity = leafCapacity;
            state.NodeCapacity = m_World.GetBufferLength<BvhNode>(state.BvhNodes);
            state.ThreadCapacity = threadCapacity;
            state.PairCapacity = m_World.GetBufferLength<CandidatePair>(state.CandidatePairs);
            state.ContactCapacity = m_World.GetBufferLength<ContactManifold>(state.Contacts);
            state.ContactPairCapacity = m_World.GetBufferLength<ContactPairRecord>(state.CurrentContactPairs);
            state.ContactEventCapacity = m_World.GetBufferLength<ContactEvent>(state.ContactEvents);
        }

        /// <summary>向上取整到 2 的幂（0 → 1）。</summary>
        public static int NextPowerOfTwo(int value)
        {
            if (value <= 1) return 1;
            int result = 1;
            while (result < value) result <<= 1;
            return result;
        }

        /// <summary>增长单个 buffer 到至少 <paramref name="length"/>（按 2 的幂取整）。</summary>
        private void Grow<T>(ref BufferHandle handle, int length) where T : unmanaged
        {
            int current = m_World.GetBufferLength<T>(handle);
            if (current >= length) return;

            int target = 4;
            while (target < length) target <<= 1;

            m_World.ResizeBuffer<T>(handle, target);
        }

        /// <summary>
        /// 依赖实际 pair 数的容量（阶段 A 同步点之后调用）：
        /// 候选 pair 数组按精确数量取整扩容，接触计数 / 偏移 / 扫描块按 pair 数扩容。
        /// 这样避免为「最坏情况 pair 数」预留数百 MB。
        /// </summary>
        public void EnsurePairDependentCapacity(int pairCount, int contactCapacityHint)
        {
            ref var state = ref MutableState;

            Grow<CandidatePair>(ref state.CandidatePairs, math.max(1, pairCount));
            int grownPairCapacity = m_World.GetBufferLength<CandidatePair>(state.CandidatePairs);
            Grow<int>(ref state.ContactCounts, grownPairCapacity);
            Grow<int>(ref state.ContactOffsets, grownPairCapacity + 1);
            Grow<int>(ref state.ContactScanBlocks, BlockScan.BlockCount(math.max(1, pairCount), ScanBlockSize) + 1);
            Grow<ContactManifold>(ref state.Contacts, math.max(16, contactCapacityHint));

            state.PairCapacity = m_World.GetBufferLength<CandidatePair>(state.CandidatePairs);
            state.ContactCapacity = m_World.GetBufferLength<ContactManifold>(state.Contacts);
        }

        /// <summary>
        /// 依赖实际接触数的容量（阶段 B 同步点之后调用）。
        /// </summary>
        public void EnsureContactCapacity(int contactCount)
        {
            ref var state = ref MutableState;
            Grow<ContactManifold>(ref state.Contacts, math.max(1, contactCount));
            state.ContactCapacity = m_World.GetBufferLength<ContactManifold>(state.Contacts);
        }

        /// <summary>按本帧和上一帧 pair 数扩容 P3 事件缓冲。</summary>
        public void EnsureContactEventCapacity(int currentCount, int previousCount)
        {
            ref var state = ref MutableState;
            int pairCapacity = math.max(1, math.max(currentCount, previousCount));
            int eventCapacity = math.max(1, currentCount + previousCount);
            Grow<ContactPairRecord>(ref state.PreviousContactPairs, pairCapacity);
            Grow<ContactPairRecord>(ref state.CurrentContactPairs, pairCapacity);
            Grow<ContactPairRecord>(ref state.ContactPairScratch, pairCapacity);
            Grow<ContactEvent>(ref state.ContactEvents, eventCapacity);
            state.ContactPairCapacity = m_World.GetBufferLength<ContactPairRecord>(state.CurrentContactPairs);
            state.ContactEventCapacity = m_World.GetBufferLength<ContactEvent>(state.ContactEvents);
        }

        /// <summary>
        /// 把 Job 侧粘滞诊断标志汇总为单帧溢出计数。
        /// Job 用「按槽位写 1」而非原子自增：同一槽被多线程重复写同一个值是幂等的，
        /// 因此既能发现溢出，又不引入原子操作（Burst 友好、无竞争）。
        /// </summary>
        public unsafe void AccumulateDiagnostics()
        {
            var flags = (int*)DiagnosticFlagPtr;
            if (flags == null) return;

            int overflow = 0;
            for (int i = 0; i < CollisionWorld.DiagnosticSlotCount; i++)
            {
                if (flags[i] != 0) overflow++;
            }

            ref var state = ref MutableState;
            state.OverflowCount = overflow;
        }

        /// <summary>设置 Chunk 元数据表容量。</summary>
        public void EnsureChunkCapacity(int chunkCount)
        {
            ref var state = ref MutableState;
            Grow<ChunkInfo>(ref state.ChunkInfos, math.max(1, chunkCount));
            Grow<byte>(ref state.ChunkStaticFlags, math.max(1, chunkCount));
            state.ChunkCapacity = m_World.GetBufferLength<ChunkInfo>(state.ChunkInfos);
        }

        /// <summary>逐 Chunk 静态标志（由串行侧依据 Static 标签的 Chunk 归属填写）。</summary>
        internal readonly long ChunkStaticFlagPtr =>
            NativePointer<byte>(State.ChunkStaticFlags, State.ChunkCount);

        /// <summary>写入某 Chunk 的静态标志（串行侧调用）。</summary>
        public void SetChunkStatic(int chunkIndex, bool isStatic)
        {
            ref var state = ref MutableState;
            if (chunkIndex < 0 || chunkIndex >= state.ChunkCount) return;
            BufferSpan<byte> span = m_World.GetBuffer<byte>(state.ChunkStaticFlags);
            if (chunkIndex >= span.Length) return;
            span[chunkIndex] = (byte)(isStatic ? 1 : 0);
        }

        // ---------------------------------------------------------------------
        // 每帧串行准备：采集列指针表 + 稠密基址
        // ---------------------------------------------------------------------

        /// <summary>
        /// 采集每个 Chunk 的列指针与稠密下标基址，返回稠密 body 总数。
        /// 仅在串行侧调用。要求查询已保证各列存在（由 <c>CollisionSetupSystem</c> 负责）。
        /// </summary>
        public unsafe int FillChunkInfos(in ReadOnlyChunkList chunks)
        {
            ref var state = ref MutableState;
            int chunkCount = chunks.Count;
            state.ChunkCount = chunkCount;
            state.SortBlockCount = math.max(1, (chunkCount + SortBlockSize - 1) / SortBlockSize);

            if (chunkCount == 0)
            {
                state.BodyCount = 0;
                return 0;
            }

            var infos = m_World.GetBuffer<ChunkInfo>(state.ChunkInfos);
            int baseIndex = 0;
            for (int i = 0; i < chunkCount; i++)
            {
                Chunk chunk = chunks[i];
                int count = chunk.Count;

                var info = new ChunkInfo
                {
                    ColliderPtr = (long)chunk.GetColumn<Collider>().UnsafePtr,
                    BodyPtr = (long)chunk.GetColumn<CollisionBody>().UnsafePtr,
                    FilterPtr = (long)chunk.GetColumn<CollisionFilter>().UnsafePtr,
                    LocalToWorldPtr = (long)chunk.GetColumn<LocalToWorld>().UnsafePtr,
                    BoundsPtr = (long)chunk.GetColumn<BoundingVolume>().UnsafePtr,
                    StatePtr = (long)chunk.GetColumn<CollisionState>().UnsafePtr,
                    Count = count,
                    Base = baseIndex,
                };

                infos[i] = info;
                baseIndex += count;
            }

            state.BodyCount = baseIndex;
            return baseIndex;
        }

        /// <summary>重置当帧计数器（每帧串行准备阶段调用）。</summary>
        public void ResetFrameCounters()
        {
            ref var state = ref MutableState;
            state.CandidatePairCount = 0;
            state.ContactCount = 0;
            state.InternalNodeCount = 0;
            state.QueryReady = 0;
            state.OverflowCount = 0;

            var diagnostics = m_World.GetBuffer<int>(state.Diagnostics);
            for (int i = 0; i < CollisionWorld.DiagnosticSlotCount && i < diagnostics.Length; i++)
                diagnostics[i] = 0;

            var flags = m_World.GetBuffer<int>(state.DiagnosticFlags);
            for (int i = 0; i < CollisionWorld.DiagnosticSlotCount && i < flags.Length; i++)
                flags[i] = 0;
        }

        // ---------------------------------------------------------------------
        // Job 视图（必须在全部扩容之后调用）
        // ---------------------------------------------------------------------

        /// <summary>Chunk 元数据表。</summary>
        internal readonly long ChunkInfoPtr =>
            NativePointer<ChunkInfo>(State.ChunkInfos, State.ChunkCount);

        /// <summary>稠密 body 实体句柄。</summary>
        internal readonly long BodyEntityPtr => NativePointer<Entity>(State.BodyEntities, State.BodyCount);

        /// <summary>稠密 body 世界包围盒。</summary>
        internal readonly long BodyBoundsPtr => NativePointer<Aabb>(State.BodyBounds, State.BodyCount);

        /// <summary>稠密 body 位姿。</summary>
        internal readonly long BodyPosePtr => NativePointer<BodyPose>(State.BodyPoses, State.BodyCount);

        /// <summary>稠密 body 形状。</summary>
        internal readonly long BodyColliderPtr => NativePointer<Collider>(State.BodyColliders, State.BodyCount);

        /// <summary>稠密 body 过滤器。</summary>
        internal readonly long BodyFilterPtr =>
            NativePointer<CollisionFilter>(State.BodyFilters, State.BodyCount);

        /// <summary>稠密 body 标志位。</summary>
        internal readonly long BodyFlagPtr => NativePointer<byte>(State.BodyFlags, State.BodyCount);

        /// <summary>稠密 body 的本帧接触标志（P2 count Job 写入）。</summary>
        internal readonly long BodyContactFlagPtr =>
            NativePointer<byte>(State.BodyContactFlags, State.BodyCount);

        /// <summary>稠密下标 → chunk 下标。</summary>
        internal readonly long BodyChunkPtr => NativePointer<int>(State.BodyChunks, State.BodyCount);

        /// <summary>Morton 键。</summary>
        internal readonly long MortonKeyPtr => NativePointer<uint>(State.MortonKeys, State.BodyCount);

        /// <summary>按 Morton 排序后的稠密下标。</summary>
        internal readonly long BodyOrderPtr => NativePointer<int>(State.BodyOrder, State.BodyCount);

        /// <summary>Morton 键双缓冲目标。</summary>
        internal readonly long MortonKeyScratchPtr =>
            NativePointer<uint>(State.MortonKeysScratch, State.BodyCount);

        /// <summary>基数排序双缓冲目标。</summary>
        internal readonly long BodyOrderScratchPtr =>
            NativePointer<int>(State.BodyOrderScratch, State.BodyCount);

        /// <summary>每叶候选 pair 计数。</summary>
        internal readonly long PairCountPtr => NativePointer<int>(State.PairCounts, State.BodyCount);

        /// <summary>pair 前缀和（长度 = bodyCount + 1）。</summary>
        internal readonly long PairOffsetPtr => NativePointer<int>(State.PairOffsets, State.BodyCount + 1);

        /// <summary>基数排序分块直方图。</summary>
        internal readonly long BucketHistogramPtr =>
            NativePointer<int>(State.BucketHistogram, State.SortBlockCount * 256);

        /// <summary>基数排序分块偏移。</summary>
        internal readonly long BucketOffsetPtr =>
            NativePointer<int>(State.BucketOffsets, State.SortBlockCount * 256);

        /// <summary>分块包围盒归约中间量。</summary>
        internal readonly long BlockBoundsPtr =>
            NativePointer<Aabb>(State.BlockBounds, math.max(1, (State.BodyCount + BoundsBlockSize - 1) / BoundsBlockSize));

        /// <summary>凸形状顶点池裸指针（供 Job 使用）。</summary>
        internal readonly unsafe long VertexPointer
        {
            get
            {
                BufferSpan<float3> span = m_World.GetBuffer<float3>(State.Vertices);
                return span.Length > 0 ? (long)span.UnsafePtr : 0L;
            }
        }

        /// <summary>桶总数（先存总数，原地前缀和后为桶全局起始）。</summary>
        internal readonly long BucketTotalsPtr =>
            NativePointer<int>(State.BucketTotals, RadixSort32.BucketCount);

        /// <summary>
        /// 按 Morton 排序后的稠密下标。基数排序的键 / 下标在主副数组间乒乓，
        /// 结果落在哪一侧取决于趟数奇偶（2D 为 3 趟 → 副数组；3D 为 4 趟 → 主数组），
        /// 故此处按 <see cref="CollisionWorld.SortResultInScratch"/> 动态取值——
        /// 若固定读主数组，2D 场景会读到半排序的结果，表现为<b>偶发漏检</b>。
        /// </summary>
        internal readonly long SortedOrderPtr =>
            State.SortResultInScratch != 0
                ? NativePointer<int>(State.BodyOrderScratch, State.BodyCount)
                : NativePointer<int>(State.BodyOrder, State.BodyCount);

        /// <summary>记录排序结果的存放位置（调度期由趟数奇偶确定）。</summary>
        public void SetSortResultInScratch(bool inScratch)
        {
            ref var state = ref MutableState;
            state.SortResultInScratch = (byte)(inScratch ? 1 : 0);
        }

        /// <summary>分块扫描块基址（长度 = 块数 + 1，末位存候选 pair 总数）。</summary>
        internal readonly long PairScanBlockPtr =>
            NativePointer<int>(State.PairScanBlocks,
                BlockScan.BlockCount(State.BodyCount, ScanBlockSize) + 1);

        /// <summary>分块扫描块基址（接触；末位存流形总数）。</summary>
        internal readonly long ContactScanBlockPtr =>
            NativePointer<int>(State.ContactScanBlocks,
                BlockScan.BlockCount(math.max(1, State.CandidatePairCount), ScanBlockSize) + 1);

        /// <summary>粘滞诊断标志（下标 = 诊断槽）。</summary>
        internal readonly long DiagnosticFlagPtr =>
            NativePointer<int>(State.DiagnosticFlags, CollisionWorld.DiagnosticSlotCount);

        /// <summary>BVH 节点数组。</summary>
        /// <summary>BVH 内部节点数（编辑器可视化用；叶节点紧随其后，不在此计数内）。</summary>
        public readonly int InternalNodeCount => State.InternalNodeCount;

        /// <summary>
        /// BVH 节点池指针（编辑器可视化用）。有效节点数 = <see cref="InternalNodeCount"/> + 叶节点。
        /// </summary>
        public readonly long BvhNodePtr => NativePointer<BvhNode>(State.BvhNodes, State.NodeCapacity);

        /// <summary>诊断计数（长度 = <see cref="CollisionWorld.DiagnosticSlotCount"/>）。</summary>
        internal readonly long DiagnosticPtr =>
            NativePointer<int>(State.Diagnostics, CollisionWorld.DiagnosticSlotCount);

        /// <summary>凸形状顶点池。</summary>
        internal readonly long VertexPtr =>
            NativePointer<float3>(State.Vertices, State.VertexPoolCapacity);

        /// <summary>凸形状顶点池已用数量。</summary>
        public readonly int VertexCount => State.VertexPoolCount;

        // 视图改成裸指针后，Job 侧拿不到 NativeArray.Length，容量必须显式传入。
        // 这三个值即原 NativeView 调用里用的长度，语义不变。

        /// <summary>候选 pair buffer 容量（= <c>CandidatePairPtr</c> 可取的元素数）。</summary>
        public readonly int PairCapacity => State.PairCapacity;

        /// <summary>接触流形 buffer 容量（= <c>ContactPtr</c> 可取的元素数）。</summary>
        public readonly int ContactCapacity => State.ContactCapacity;

        /// <summary>contact pair 历史 buffer 容量。</summary>
        public readonly int ContactPairCapacity => State.ContactPairCapacity;

        /// <summary>本帧真实检测到的流形数。</summary>
        public readonly int DetectedContactCount => State.DetectedContactCount;

        /// <summary>上一帧排序后的 contact pair 数。</summary>
        public readonly int PreviousContactPairCount => State.PreviousContactPairCount;

        /// <summary>当帧接触事件数。</summary>
        public readonly int ContactEventCount => State.ContactEventCount;

        /// <summary>当前帧宽相是否已完整发布。</summary>
        public readonly bool IsQueryReady => State.QueryReady != 0;

        /// <summary>并行遍历栈。</summary>
        internal readonly long TraversalStackPtr =>
            NativePointer<int>(State.TraversalStack, State.ThreadCapacity * TraversalStackDepth);

        /// <summary>候选 pair 数组。</summary>
        public readonly long CandidatePairPtr =>
            NativePointer<CandidatePair>(State.CandidatePairs, State.PairCapacity);

        /// <summary>
        /// 每个候选 pair 产生的流形数（下标与 <see cref="CandidatePairPtr"/> 一一对应，
        /// 0 = 该候选对被窄相过滤或形状并未真正相交）。长度 = <see cref="PairCapacity"/>。
        /// 有效性约束同 <see cref="CandidatePairPtr"/>。
        ///
        /// 供调试可视化区分「宽相候选 pair」与「窄相确认的接触 pair」。
        /// </summary>
        public readonly long PairContactCountPtr =>
            NativePointer<int>(State.ContactCounts, math.max(1, State.PairCapacity));

        /// <summary>每 pair 流形计数。</summary>
        internal readonly long ContactCountPtr =>
            NativePointer<int>(State.ContactCounts, math.max(1, State.PairCapacity));

        /// <summary>接触前缀和（长度 = pairCount + 1）。</summary>
        internal readonly long ContactOffsetPtr =>
            NativePointer<int>(State.ContactOffsets, math.max(1, State.PairCapacity + 1));

        /// <summary>接触流形数组。</summary>
        public readonly long ContactPtr =>
            NativePointer<ContactManifold>(State.Contacts, State.ContactCapacity);

        internal readonly long PreviousContactPairPtr =>
            NativePointer<ContactPairRecord>(State.PreviousContactPairs, State.ContactPairCapacity);

        internal readonly long CurrentContactPairPtr =>
            NativePointer<ContactPairRecord>(State.CurrentContactPairs, State.ContactPairCapacity);

        internal readonly long ContactPairScratchPtr =>
            NativePointer<ContactPairRecord>(State.ContactPairScratch, State.ContactPairCapacity);

        internal readonly long ContactEventPtr =>
            NativePointer<ContactEvent>(State.ContactEvents, State.ContactEventCapacity);

        /// <summary>
        /// 当前帧 LBVH 的 AABB overlap 查询。结果追加到调用方容器；返回 false 不保留部分结果。
        /// 这是 broad query，命中不等同于精确 shape overlap。
        /// 只能在主线程、宽相完整发布后调用；不支持并发查询。
        /// </summary>
        public unsafe bool OverlapAabb(in Aabb queryBounds, uint belongsToMask, ref NativeList<Entity> results)
        {
            // State 是「按值拷贝的单例快照」，每个属性访问都会重新解析一次
            // （World.GetComponent + 整结构体拷贝），NativePointer 还要再取一次 buffer。
            // 一次 LBVH 查询要用近十个这样的属性，解析开销会盖过遍历本身，因此只解析一次。
            CollisionWorld state = State;
            if (state.QueryReady == 0) return false;
            int bodyCount = state.BodyCount;
            if (bodyCount <= 0 || queryBounds.IsEmpty) return true;

            var nodes = (BvhNode*)NativePointer<BvhNode>(state.BvhNodes, state.NodeCapacity);
            var order = (int*)NativePointer<int>(
                state.SortResultInScratch != 0 ? state.BodyOrderScratch : state.BodyOrder, bodyCount);
            var entities = (Entity*)NativePointer<Entity>(state.BodyEntities, bodyCount);
            var filters = (CollisionFilter*)NativePointer<CollisionFilter>(state.BodyFilters, bodyCount);
            var flags = (byte*)NativePointer<byte>(state.BodyFlags, bodyCount);
            var stack = (int*)NativePointer<int>(
                state.TraversalStack, state.ThreadCapacity * TraversalStackDepth);
            if (nodes == null || order == null || stack == null) return false;

            int initialResultCount = results.Length;
            int stackCapacity = TraversalStackDepth;
            int root = BvhBuilder.RootIndex(state.LeafCapacity);
            int sp = 0;
            stack[sp++] = root;
            while (sp > 0)
            {
                int nodeIndex = stack[--sp];
                // ref readonly：BvhNode 是 48 字节，按值读会被降级成 memcpy；
                // 每次查询要访问数百个节点，这份拷贝在 profiler 里非常显眼。
                ref readonly BvhNode node = ref nodes[nodeIndex];
                if (!Aabb.Overlaps(node.Bounds, queryBounds)) continue;

                if (node.Right < 0)
                {
                    if (node.MaxLeaf < 0) continue;
                    int body = order[node.MaxLeaf];
                    if (IsQueryable(body, bodyCount, belongsToMask, filters, flags)) results.Add(entities[body]);
                    continue;
                }

                if (sp + 2 > stackCapacity)
                {
                    results.Length = initialResultCount;
                    return false;
                }
                stack[sp++] = node.Left;
                stack[sp++] = node.Right;
            }

            return true;
        }

        /// <summary>当前帧 LBVH 的最近 AABB 射线查询，不执行精确 shape cast。仅主线程非并发调用。</summary>
        public unsafe bool RaycastAabb(
            float3 origin,
            float3 direction,
            float maxDistance,
            uint belongsToMask,
            out CollisionRaycastHit hit)
        {
            hit = default;
            // 同上：一次性解析状态与全部裸指针。
            CollisionWorld state = State;
            if (state.QueryReady == 0) return false;
            int bodyCount = state.BodyCount;
            if (bodyCount <= 0 || maxDistance < 0f) return false;
            float directionLengthSq = math.lengthsq(direction);
            if (directionLengthSq <= 1e-12f) return false;

            float3 normalized = direction * math.rsqrt(directionLengthSq);
            float3 end = origin + normalized * maxDistance;
            var segmentBounds = new Aabb
            {
                Min = math.min(origin, end),
                Max = math.max(origin, end),
            };
            var nodes = (BvhNode*)NativePointer<BvhNode>(state.BvhNodes, state.NodeCapacity);
            var order = (int*)NativePointer<int>(
                state.SortResultInScratch != 0 ? state.BodyOrderScratch : state.BodyOrder, bodyCount);
            var bounds = (Aabb*)NativePointer<Aabb>(state.BodyBounds, bodyCount);
            var entities = (Entity*)NativePointer<Entity>(state.BodyEntities, bodyCount);
            var filters = (CollisionFilter*)NativePointer<CollisionFilter>(state.BodyFilters, bodyCount);
            var flags = (byte*)NativePointer<byte>(state.BodyFlags, bodyCount);
            var stack = (int*)NativePointer<int>(
                state.TraversalStack, state.ThreadCapacity * TraversalStackDepth);
            if (nodes == null || order == null || stack == null) return false;

            int stackCapacity = TraversalStackDepth;
            int root = BvhBuilder.RootIndex(state.LeafCapacity);
            int sp = 0;
            float nearest = maxDistance;
            bool found = false;
            stack[sp++] = root;
            while (sp > 0)
            {
                int nodeIndex = stack[--sp];
                ref readonly BvhNode node = ref nodes[nodeIndex];
                if (!Aabb.Overlaps(node.Bounds, segmentBounds)) continue;

                if (node.Right < 0)
                {
                    if (node.MaxLeaf < 0) continue;
                    int body = order[node.MaxLeaf];
                    if (!IsQueryable(body, bodyCount, belongsToMask, filters, flags)) continue;
                    if (!CollisionQueryMath.TryRayAabb(origin, normalized, nearest, bounds[body], out float distance, out float3 normal)) continue;

                    nearest = distance;
                    found = true;
                    hit = new CollisionRaycastHit
                    {
                        Entity = entities[body],
                        Distance = distance,
                        Position = origin + normalized * distance,
                        Normal = normal,
                    };
                    continue;
                }

                if (sp + 2 > stackCapacity)
                {
                    hit = default;
                    return false;
                }
                stack[sp++] = node.Left;
                stack[sp++] = node.Right;
            }

            return found;
        }

        private static unsafe bool IsQueryable(
            int body,
            int bodyCount,
            uint belongsToMask,
            CollisionFilter* filters,
            byte* flags)
        {
            byte participation = (byte)(CollisionBody.EnabledBit | CollisionBody.ActiveBit);
            return body >= 0
                && body < bodyCount
                && (filters[body].BelongsTo & belongsToMask) != 0
                && (flags[body] & participation) == participation;
        }

        /// <summary>
        /// 向凸形状顶点池追加顶点，返回起始下标。
        /// 用于 <c>Polygon2D</c> / <c>Convex</c>：形状参数只存
        /// (VertexStart, VertexCount)，顶点几何集中在顶点池里，
        /// 便于多个碰撞体共享同一份多边形数据。
        /// </summary>
        public int AppendVertices(float3[] vertices)
        {
            if (vertices == null || vertices.Length == 0) return -1;

            EnsureInitialized();

            ref var state = ref MutableState;
            int start = state.VertexPoolCount;
            Grow<float3>(ref state.Vertices, start + vertices.Length);
            state.VertexPoolCapacity = m_World.GetBufferLength<float3>(state.Vertices);
            state.VertexPoolCount = start + vertices.Length;

            BufferSpan<float3> span = m_World.GetBuffer<float3>(state.Vertices);
            for (int i = 0; i < vertices.Length; i++)
                span[start + i] = vertices[i];

            return start;
        }

        /// <summary>写回当帧计数（串行阶段调用）。</summary>
        public void SetFrameResults(int candidatePairCount, int contactCount, int internalNodeCount)
        {
            ref var state = ref MutableState;
            state.CandidatePairCount = candidatePairCount;
            state.ContactCount = contactCount;
            state.InternalNodeCount = internalNodeCount;
            state.QueryReady = 1;
        }

        public void SetDetectedContactCount(int count)
        {
            MutableState.DetectedContactCount = math.max(0, count);
        }

        /// <summary>提交 P3 归并结果，并交换 current/previous pair buffer。</summary>
        public void CommitContactEvents(int currentPairCount, int eventCount)
        {
            ref var state = ref MutableState;
            BufferHandle swap = state.PreviousContactPairs;
            state.PreviousContactPairs = state.CurrentContactPairs;
            state.CurrentContactPairs = swap;
            state.PreviousContactPairCount = currentPairCount;
            state.ContactEventCount = eventCount;
        }

        /// <summary>累加容量溢出（Job 无法直接告警，先计数后串行汇总）。</summary>
        public void AddOverflow(int count)
        {
            if (count <= 0) return;
            ref var state = ref MutableState;
            state.OverflowCount += count;
        }

        /// <summary>
        /// 取 World buffer 的裸指针供 Job 使用（0 表示不可用）。
        ///
        /// 刻意<b>不</b>返回 <see cref="NativeArray{T}"/>：由裸内存构造的 NativeArray
        /// 其安全句柄是 <c>default</c>（ConvertExistingDataToNativeArray 不设 m_Safety），
        /// 作为 Job 容器字段会在调度期被拒绝，在主线程索引会解引用空句柄节点。
        /// 详见 <see cref="NativeBufferUtil"/> 的说明。
        /// </summary>
        private readonly unsafe long NativePointer<T>(
            BufferHandle handle, int length, [CallerMemberName] string member = null) where T : unmanaged
        {
            // 长度为 0 是合法请求（当帧没有 body / 没有 pair），调用方不会拿它去调度 Job。
            if (length <= 0) return 0L;

            // 以下是「要了但拿不到」——必须响，不能返回 0。
            // 返回 0 会让 Job 拿到空指针，在托管路径下退化成无栈的 NullReferenceException，
            // 拿到指针之前 NativeArray 索引器会替我们报错的那一层就没了。
            if (handle.IsNull)
                throw new InvalidOperationException(
                    $"CollisionWorldView.{member}: buffer handle is not initialized " +
                    $"(requested {length} x {typeof(T).Name}).");

            BufferSpan<T> span = m_World.GetBuffer<T>(handle);
            if (span.Length < length)
                throw new InvalidOperationException(
                    $"CollisionWorldView.{member}: buffer too small — requested {length} x {typeof(T).Name}, " +
                    $"buffer holds {span.Length}. The growth expression in EnsureCapacity / " +
                    $"EnsurePairDependentCapacity does not match the length this accessor asks for.");

            return (long)span.UnsafePtr;
        }
    }
}
