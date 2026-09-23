using System.Collections.Generic;
using Ember.Core;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Ember.Collision
{
    /// <summary>
    /// 碰撞<b>宽相</b>系统（串行外壳 + 内部并行 Burst 流水线）。
    ///
    /// 为什么是 <see cref="SystemBase"/> 而不是 <c>JobSystem&lt;T&gt;</c>：
    /// 框架的 <c>JobSystem&lt;TJob&gt;</c> 只能表达「每个 Chunk 跑一次」的
    /// <c>IEmberChunkJob</c>；而碰撞管线的中段是按 <b>pair / 流形</b>并行，
    /// 且阶段之间要传递中间缓冲，无法用 chunk 并行的接口表达。
    /// 因此这里自建链式 <c>JobHandle</c> 流水线并在 <c>OnTick</c> 内完成——
    /// 满足框架「<b>不允许跨 tick 挂起 Job</b>」的硬约束
    /// （<c>WorldSafety</c> 为 internal，外部模块无法注册在飞 Job，
    /// 若跨 tick 挂起会让框架的结构变更保护失效）。
    ///
    /// 流水线（节点数 = 每次调度一个 Job）：
    /// <code>
    /// Gather（按 Chunk）→ BoundsReduce（分块）→ BoundsReduceFinal（串行）
    ///   → Morton（按 body）→ RadixSort ×4 趟 ×4 步 → BvhLeaf（按叶）
    ///   → BvhMerge（逐层）→ BvhFinalize（串行）→ PairCount（按叶）
    ///   → ScanBlock / ScanBlocks / ScanAdd（pair 偏移）
    /// </code>
    /// 之后同步一次读出<b>精确</b> pair 数，再按精确容量扩容并写入 pair 数组。
    /// 同步点换来的是「不为最坏情况预留数百 MB」；消除它需要
    /// 「按上帧数量预测 + 溢出重跑」，属已记录的 P5 优化项。
    /// </summary>
    public sealed class CollisionBroadphaseSystem : SystemBase
    {
        /// <summary>无碰撞体时的 pair 容量下限。</summary>
        private const int MinPredictedPairs = 1024;

        /// <summary>首帧接触流形容量提示；P2 计数后会按精确数量扩容。</summary>
        private const int InitialContactCapacity = MinPredictedPairs;

        // 查询在 OnCreate 构造，不用字段初始化器：系统由 SystemTicker.Register 立即构造，
        // 早于 ECSManager.Start()、早于 World 构造，而 ComponentMask.With<T>() 会当场读组件注册表。
        private EntityQuery m_ColliderQuery;
        private EntityQuery m_StaticChunkQuery;

        /// <summary>带 Static 标签的 Chunk 集合（Tag 是 Archetype 级，故只需 Chunk 级归属判定）。
        /// 复用同一个 HashSet 并在每帧 Clear，稳态零 GC。</summary>
        private readonly HashSet<Chunk> m_StaticChunks = new();

        public override void OnCreate()
        {
            m_ColliderQuery = new EntityQuery(
                new ComponentMask()
                    .With<Collider>()
                    .With<CollisionBody>()
                    .With<CollisionFilter>()
                    .With<CollisionState>()
                    .With<LocalToWorld>()
                    .With<BoundingVolume>(),
                ComponentMask.Empty,
                new ComponentMask().With<Prefab>().With<Disabled>());

            m_StaticChunkQuery = new EntityQuery(
                new ComponentMask().With<Collider>().With<Static>(),
                ComponentMask.Empty,
                new ComponentMask().With<Prefab>());
        }

        private Entity m_Owner;
        private CollisionWorldView m_View;

        /// <summary>上帧 pair 数（容量预测，避免每帧按最坏情况预留）。</summary>
        private int m_PredictedPairs = MinPredictedPairs;

        /// <summary>上帧 pair 数（供诊断 / 测试读取）。</summary>
        public int LastPairCount { get; private set; }

        protected override void DeclareAccess(AccessBuilder access) => access
            .Read<Collider>()
            .Read<CollisionBody>()
            .Read<CollisionFilter>()
            .Read<CollisionState>()
            .Read<CollisionConfig>()
            .Read<Static>()
            .Read<LocalToWorld>()
            .Write<BoundingVolume>()
            .Write<CollisionWorld>()
            .StructuralChanges(); // 首次 tick 创建 CollisionWorld 单例实体

        protected override unsafe void OnTick(SystemContext ctx)
        {
            World world = ctx.World;
            m_Owner = world.GetOrCreateSingleton<CollisionWorld>();
            m_View = new CollisionWorldView(world, m_Owner);
            m_View.EnsureInitialized();

            CollisionConfig config = world.TryGetSingleton<CollisionConfig>(out Entity configOwner)
                ? world.GetComponent<CollisionConfig>(configOwner)
                : CollisionConfig.Default;

            ReadOnlyChunkList chunks = world.CompileQuery(m_ColliderQuery).GetChunks();

            m_View.EnsureChunkCapacity(chunks.Count);
            m_View.ResetFrameCounters();
            int bodyCount = m_View.FillChunkInfos(chunks);
            FillChunkStaticFlags(world, chunks);

            if (bodyCount <= 0)
            {
                LastPairCount = 0;
                m_View.SetFrameResults(0, 0, 0);
                return;
            }

            // 按上帧 pair 数预测容量：预测够用时整条链只需**一次**主线程停等。
            m_View.EnsureCapacity(bodyCount, chunks.Count, m_PredictedPairs, InitialContactCapacity);

            int leafCapacity = m_View.LeafCapacity;
            int scanBlocks = BlockScan.BlockCount(bodyCount, CollisionWorldView.ScanBlockSize);

            // ---------- 一次性排完整条链：采集 → 归约 → 排序 → BVH → pair 计数 → 前缀和 → 写入 pair ----------
            // 这里只 .Complete() 一次；旧实现把「计数/前缀和」与「写入 pair」拆成两段、
            // 中间停下读精确 pair 数再扩容，等于每帧多排空一次主线程。
            JobHandle chain = ScheduleBroadphase(config, bodyCount, leafCapacity, chunks.Count);
            chain = SchedulePairCollect(config, bodyCount, leafCapacity, chain);
            chain.Complete();

            // 读出精确 pair 数。计数趟只写 PairCounts[bodyCount]，不受输出容量影响，所以始终精确。
            var pairScanBlocks = (int*)m_View.PairScanBlockPtr;
            var diagnostics = (int*)m_View.DiagnosticFlagPtr;
            bool countOverflow = diagnostics != null
                && diagnostics[CollisionWorld.DiagPairStackOverflow] != 0;
            int pairCount = !countOverflow && pairScanBlocks != null ? pairScanBlocks[scanBlocks] : 0;
            if (pairCount < 0) pairCount = 0;

            // 预测容量不足时补跑一次：按精确总数扩容后重跑收集。
            //
            // 完整性判据就是「精确总数 <= 容量」：前缀和每一项就是该叶要写的数量，
            // 容量足够时收集必然写满，不需要哨兵（哨兵反而会破坏补跑所需的 expected）。
            // 扩容后容量恰好等于精确总数，所以补跑一次必然成功，不存在循环重试。
            if (pairCount > m_View.PairCapacity)
            {
                m_View.EnsurePairDependentCapacity(pairCount, InitialContactCapacity);
                SchedulePairCollect(config, bodyCount, leafCapacity, default).Complete();
            }

            m_View.EnsurePairDependentCapacity(pairCount, InitialContactCapacity);
            m_PredictedPairs = math.max(MinPredictedPairs, pairCount * 2);
            LastPairCount = pairCount;
            m_View.SetFrameResults(pairCount, 0, 0);
            m_View.AccumulateDiagnostics();
            WarnOnDiagnostics();
        }

        /// <summary>
        /// 填写逐 Chunk 静态标志。
        ///
        /// 为什么是 Chunk 级而非实体级：Ember 的 Tag 组件只占 Archetype 掩码、没有列，
        /// 所以同一 Chunk 内所有实体的 Static 归属必然一致。于是只需对
        /// 「带 Static 标签的 Chunk 集合」做一次 O(Chunk 数) 的归属判定，
        /// 而<b>不需要</b>每帧对全部实体做 O(实体数) 的标签比对
        /// （那在 1M 实体场景下是每帧毫秒级的串行开销）。
        /// </summary>
        private void FillChunkStaticFlags(World world, in ReadOnlyChunkList chunks)
        {
            m_StaticChunks.Clear();
            ReadOnlyChunkList staticChunks = world.CompileQuery(m_StaticChunkQuery).GetChunks();
            for (int i = 0; i < staticChunks.Count; i++)
                m_StaticChunks.Add(staticChunks[i]);

            for (int i = 0; i < chunks.Count; i++)
                m_View.SetChunkStatic(i, m_StaticChunks.Contains(chunks[i]));
        }

        /// <summary>宽相全链路：采集 → 归约 → 编码 → 排序 → BVH → pair 计数与偏移。</summary>
        private JobHandle ScheduleBroadphase(
            in CollisionConfig config, int bodyCount, int leafCapacity, int chunkCount)
        {
            int boundsBlocks = BlockScan.BlockCount(bodyCount, CollisionWorldView.BoundsBlockSize);
            int sortBlocks = BlockScan.BlockCount(bodyCount, CollisionWorldView.SortBlockSize);
            int scanBlocks = BlockScan.BlockCount(bodyCount, CollisionWorldView.ScanBlockSize);
            int keyBits = config.Dimension == CollisionDimension.XYZ ? 30 : 20;

            JobHandle handle = new GatherJob
            {
                ChunkInfosPtr = m_View.ChunkInfoPtr,
                ChunkStaticFlagsPtr = m_View.ChunkStaticFlagPtr,
                Dimension = config.Dimension,
                VertexPtr = m_View.VertexPointer,
                VertexPoolLength = m_View.VertexCount,
                BodyEntitiesPtr = m_View.BodyEntityPtr,
                BodyBoundsPtr = m_View.BodyBoundsPtr,
                BodyPosesPtr = m_View.BodyPosePtr,
                BodyCollidersPtr = m_View.BodyColliderPtr,
                BodyFiltersPtr = m_View.BodyFilterPtr,
                BodyFlagsPtr = m_View.BodyFlagPtr,
                BodyChunksPtr = m_View.BodyChunkPtr,
            }.Schedule(chunkCount, 8, default);

            handle = new BoundsReduceJob
            {
                BodyBoundsPtr = m_View.BodyBoundsPtr,
                BlockBoundsPtr = m_View.BlockBoundsPtr,
                BodyCount = bodyCount,
                BlockSize = CollisionWorldView.BoundsBlockSize,
            }.Schedule(boundsBlocks, 4, handle);

            handle = new BoundsReduceFinalJob
            {
                BlockBoundsPtr = m_View.BlockBoundsPtr,
                BlockCount = boundsBlocks,
            }.Schedule(handle);

            handle = new MortonJob
            {
                BodyBoundsPtr = m_View.BodyBoundsPtr,
                BlockBoundsPtr = m_View.BlockBoundsPtr,
                BlockCount = boundsBlocks,
                Dimension = config.Dimension,
                MortonKeysPtr = m_View.MortonKeyPtr,
                BodyOrderPtr = m_View.BodyOrderPtr,
            }.Schedule(bodyCount, 64, handle);

            handle = ScheduleRadixSort(handle, bodyCount, sortBlocks, keyBits);
            m_View.SetSortResultInScratch(RadixSort32.PassCountForBits(keyBits) % 2 != 0);

            handle = new BvhLeafJob
            {
                BodyBoundsPtr = m_View.BodyBoundsPtr,
                SortedOrderPtr = m_View.SortedOrderPtr,
                NodesPtr = m_View.BvhNodePtr,
                BodyCount = bodyCount,
                LeafCapacity = leafCapacity,
            }.Schedule(leafCapacity, 64, handle);

            handle = ScheduleBvhLevels(handle, leafCapacity);

            handle = new PairCountJob
            {
                NodesPtr = m_View.BvhNodePtr,
                BodyBoundsPtr = m_View.BodyBoundsPtr,
                SortedOrderPtr = m_View.SortedOrderPtr,
                TraversalStackPtr = m_View.TraversalStackPtr,
                PairCountsPtr = m_View.PairCountPtr,
                BodyFlagsPtr = m_View.BodyFlagPtr,
                Root = BvhBuilder.RootIndex(leafCapacity),
                BodyCount = bodyCount,
                StackDepth = CollisionWorldView.TraversalStackDepth,
                // 参与位 = Enabled | Active（与窄相 IsPairEligible 同源）；
                // 关闭该策略时传 0，即不做任何过滤。
                ParticipationBits = config.SkipInactivePairs
                    ? (byte)(CollisionBody.EnabledBit | CollisionBody.ActiveBit)
                    : (byte)0,
            }.Schedule(bodyCount, 64, handle);

            handle = new PairCountNormalizeJob
            {
                PairCountsPtr = m_View.PairCountPtr,
                DiagnosticFlagsPtr = m_View.DiagnosticFlagPtr,
                BodyCount = bodyCount,
                StackOverflowSlot = CollisionWorld.DiagPairStackOverflow,
            }.Schedule(handle);

            // pair 前缀和：块内前缀(P) → 块前缀(S) → 叠加块基址(P)。
            handle = new ScanBlockJob
            {
                SourcePtr = m_View.PairCountPtr,
                DestinationPtr = m_View.PairOffsetPtr,
                BlockTotalsPtr = m_View.PairScanBlockPtr,
                Count = bodyCount,
                BlockSize = CollisionWorldView.ScanBlockSize,
            }.Schedule(scanBlocks, 1, handle);

            handle = new ScanBlocksJob
            {
                BlockTotalsPtr = m_View.PairScanBlockPtr,
                BlockCount = scanBlocks,
            }.Schedule(handle);

            return new ScanAddJob
            {
                DestinationPtr = m_View.PairOffsetPtr,
                BlockTotalsPtr = m_View.PairScanBlockPtr,
                Count = bodyCount,
                BlockSize = CollisionWorldView.ScanBlockSize,
            }.Schedule(scanBlocks, 1, handle);
        }

        /// <summary>
        /// 基数排序（4 步 × 趟数）。键与排序下标成对在「主 / 副」数组间乒乓，
        /// 因此结果可能落在任一侧，由 <see cref="CollisionWorldView.SetSortResultInScratch"/> 记录。
        /// </summary>
        private JobHandle ScheduleRadixSort(JobHandle handle, int count, int blockCount, int keyBits)
        {
            int passCount = RadixSort32.PassCountForBits(keyBits);
            bool inScratch = false;

            for (int pass = 0; pass < passCount; pass++)
            {
                int shift = RadixSort32.ShiftForPass(pass);

                long keys = inScratch ? m_View.MortonKeyScratchPtr : m_View.MortonKeyPtr;
                long order = inScratch ? m_View.BodyOrderScratchPtr : m_View.BodyOrderPtr;
                long outKeys = inScratch ? m_View.MortonKeyPtr : m_View.MortonKeyScratchPtr;
                long outOrder = inScratch ? m_View.BodyOrderPtr : m_View.BodyOrderScratchPtr;

                handle = new RadixHistogramJob
                {
                    KeysPtr = keys,
                    HistogramPtr = m_View.BucketHistogramPtr,
                    Count = count,
                    BlockSize = CollisionWorldView.SortBlockSize,
                    BlockCount = blockCount,
                    Shift = shift,
                }.Schedule(blockCount, 1, handle);

                handle = new RadixBucketPrefixJob
                {
                    HistogramPtr = m_View.BucketHistogramPtr,
                    OffsetsPtr = m_View.BucketOffsetPtr,
                    TotalsPtr = m_View.BucketTotalsPtr,
                    BlockCount = blockCount,
                }.Schedule(RadixSort32.BucketCount, 8, handle);

                handle = new RadixTotalsPrefixJob
                {
                    TotalsPtr = m_View.BucketTotalsPtr,
                }.Schedule(handle);

                handle = new RadixScatterJob
                {
                    KeysPtr = keys,
                    OrderPtr = order,
                    OffsetsPtr = m_View.BucketOffsetPtr,
                    TotalsPtr = m_View.BucketTotalsPtr,
                    OutKeysPtr = outKeys,
                    OutOrderPtr = outOrder,
                    Count = count,
                    BlockSize = CollisionWorldView.SortBlockSize,
                    BlockCount = blockCount,
                    Shift = shift,
                }.Schedule(blockCount, 1, handle);

                inScratch = !inScratch;
            }

            return handle;
        }

        /// <summary>
        /// BVH 逐层合并：宽度大于阈值的层并行调度（每父节点一次执行），
        /// 其余交给一次串行收尾——避免为零星节点付出 Job 调度开销。
        /// </summary>
        private JobHandle ScheduleBvhLevels(JobHandle handle, int leafCapacity)
        {
            int levelStart = 0;
            int levelCount = leafCapacity;
            int parentStart = leafCapacity;

            while (levelCount > 1 && levelCount > CollisionWorldView.BvhParallelWidthThreshold)
            {
                int parentCount = levelCount >> 1;

                handle = new BvhMergeJob
                {
                    NodesPtr = m_View.BvhNodePtr,
                    LevelStart = levelStart,
                    ParentStart = parentStart,
                }.Schedule(parentCount, 8, handle);

                levelStart = parentStart;
                levelCount = parentCount;
                parentStart += parentCount;
            }

            return new BvhFinalizeJob
            {
                NodesPtr = m_View.BvhNodePtr,
                LevelStart = levelStart,
                LevelCount = levelCount,
                ParentStart = parentStart,
            }.Schedule(handle);
        }

        /// <summary>按精确容量写入候选 pair（每叶一次，写区间由前缀和保证互不重叠）。</summary>
        private JobHandle SchedulePairCollect(
            in CollisionConfig config, int bodyCount, int leafCapacity, JobHandle dependency)
        {
            return new PairCollectJob
            {
                NodesPtr = m_View.BvhNodePtr,
                BodyBoundsPtr = m_View.BodyBoundsPtr,
                SortedOrderPtr = m_View.SortedOrderPtr,
                PairOffsetsPtr = m_View.PairOffsetPtr,
                PairCountsPtr = m_View.PairCountPtr,
                TraversalStackPtr = m_View.TraversalStackPtr,
                PairsPtr = m_View.CandidatePairPtr,
                BodyFlagsPtr = m_View.BodyFlagPtr,
                Root = BvhBuilder.RootIndex(leafCapacity),
                BodyCount = bodyCount,
                StackDepth = CollisionWorldView.TraversalStackDepth,
                PairCapacity = m_View.PairCapacity,
                // 参与位 = Enabled | Active（与窄相 IsPairEligible 同源）；
                // 关闭该策略时传 0，即不做任何过滤。
                ParticipationBits = config.SkipInactivePairs
                    ? (byte)(CollisionBody.EnabledBit | CollisionBody.ActiveBit)
                    : (byte)0,
            }.Schedule(bodyCount, 64, dependency);
        }

        /// <summary>汇总收集阶段的 leaf 哨兵；返回 true 时调用方必须 fail closed。</summary>
        /// <summary>
        /// 诊断汇总告警。溢出意味着当帧<b>静默丢失</b>了 pair 或接触——
        /// 这类问题在运行时表现为「偶发穿模」，必须显式暴露而非容忍。
        /// </summary>
        private unsafe void WarnOnDiagnostics()
        {
            var diagnostics = (int*)m_View.DiagnosticPtr;
            if (diagnostics == null) return;

            if (diagnostics[CollisionWorld.DiagPairStackOverflow] != 0)
            {
                Debug.LogError(
                    "[Ember.Collision] 宽相遍历栈溢出：当帧候选 pair 不完整。" +
                    $"请加大 CollisionWorldView.TraversalStackDepth（当前 {CollisionWorldView.TraversalStackDepth}）。");
            }

            if (diagnostics[CollisionWorld.DiagPairCapacityTruncated] != 0)
            {
                Debug.LogError(
                    "[Ember.Collision] 候选 pair 在扩容补跑后仍不完整：应由容量之外的原因引起，请上报此情形。");
            }
        }
    }
}
