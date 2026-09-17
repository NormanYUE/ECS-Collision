using Ember;
using Ember.Core;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Ember.Collision
{
    /// <summary>
    /// 窄相系统：候选 pair -> 精确流形计数 -> 前缀和 -> 精确容量写入。
    /// 所有 Job 在当前 tick 内完成，因为 Ember 外部模块不能跨 tick 持有在飞 Job。
    /// </summary>
    public sealed class CollisionNarrowphaseSystem : SystemBase
    {
        // 查询在 OnCreate 构造，不用字段初始化器：系统由 SystemTicker.Register 立即构造，
        // 早于 ECSManager.Start()、早于 World 构造，而 ComponentMask.With<T>() 会当场读组件注册表。
        private EntityQuery m_DisabledStateQuery;

        /// <summary>最近一帧发布的接触流形数，便于诊断和宿主侧测试。</summary>
        public int LastContactCount { get; private set; }

        public override void OnCreate()
        {
            m_DisabledStateQuery = new EntityQuery(
                new ComponentMask().With<Collider>().With<CollisionState>().With<Disabled>(),
                ComponentMask.Empty,
                new ComponentMask().With<Prefab>());
        }

        protected override void DeclareAccess(AccessBuilder access) => access
            .Read<CollisionConfig>()
            .Read<Disabled>()
            .Write<CollisionState>()
            .Write<CollisionWorld>();

        protected override void OnTick(SystemContext ctx)
        {
            if (!ctx.World.TryGetCollisionWorld(out CollisionWorldView view))
            {
                LastContactCount = 0;
                return;
            }

            int bodyCount = view.BodyCount;
            JobHandle clearFlags = new ContactFlagClearJob
            {
                BodyContactFlags = view.BodyContactFlagArray,
            }.Schedule(bodyCount, 64, default);

            int pairCount = view.CandidatePairCount;
            if (pairCount <= 0)
            {
                ScheduleStateWrite(view, clearFlags).Complete();
                AdvanceDisabledStates(ctx);
                LastContactCount = 0;
                view.SetDetectedContactCount(0);
                view.SetFrameResults(0, 0, 0);
                return;
            }

            CollisionConfig config = ctx.World.TryGetSingleton<CollisionConfig>(out Entity configOwner)
                ? ctx.World.GetComponent<CollisionConfig>(configOwner)
                : CollisionConfig.Default;
            int scanBlocks = BlockScan.BlockCount(pairCount, CollisionWorldView.ScanBlockSize);

            // Count and scan must finish before exact contact capacity can grow buffers.
            ScheduleContactCounts(view, config, pairCount, clearFlags)
                .Complete();

            NativeArray<int> scanTotals = view.ContactScanBlockArray;
            int exactCount = scanTotals.IsCreated ? math.max(0, scanTotals[scanBlocks]) : 0;
            int outputLimit = math.min(exactCount, math.max(0, config.MaxContacts));
            view.EnsureContactCapacity(outputLimit);
            view.SetDetectedContactCount(exactCount);

            if (exactCount > outputLimit)
            {
                NativeArray<int> diagnosticFlags = view.DiagnosticFlagArray;
                diagnosticFlags[CollisionWorld.DiagContactCapacityTruncated] = 1;
            }

            if (outputLimit > 0)
            {
                JobHandle collect = ScheduleContactCollect(view, config, pairCount, outputLimit);
                JobHandle.CombineDependencies(ScheduleContactFlagMark(view, pairCount), collect).Complete();
            }
            else
            {
                ScheduleContactFlagMark(view, pairCount).Complete();
            }

            ScheduleStateWrite(view, default).Complete();
            AdvanceDisabledStates(ctx);

            LastContactCount = outputLimit;
            view.SetFrameResults(pairCount, outputLimit, 0);
            view.AccumulateDiagnostics();
            WarnOnDiagnostics(view);
        }

        private static JobHandle ScheduleContactCounts(
            in CollisionWorldView view,
            in CollisionConfig config,
            int pairCount,
            JobHandle dependency)
        {
            int scanBlocks = BlockScan.BlockCount(pairCount, CollisionWorldView.ScanBlockSize);
            JobHandle handle = new ContactCountJob
            {
                Pairs = view.CandidatePairArray,
                BodyPoses = view.BodyPoseArray,
                BodyColliders = view.BodyColliderArray,
                BodyFilters = view.BodyFilterArray,
                BodyFlags = view.BodyFlagArray,
                Vertices = view.VertexArray,
                ContactCounts = view.ContactCountArray,
                BodyCount = view.BodyCount,
                VertexCount = view.VertexCount,
                Dimension = config.Dimension,
                SkipStaticPairs = config.SkipStaticPairs,
            }.Schedule(pairCount, 64, dependency);

            handle = new ScanBlockJob
            {
                Source = view.ContactCountArray,
                Destination = view.ContactOffsetArray,
                BlockTotals = view.ContactScanBlockArray,
                Count = pairCount,
                BlockSize = CollisionWorldView.ScanBlockSize,
            }.Schedule(scanBlocks, 1, handle);

            handle = new ScanBlocksJob
            {
                BlockTotals = view.ContactScanBlockArray,
                BlockCount = scanBlocks,
            }.Schedule(handle);

            return new ScanAddJob
            {
                Destination = view.ContactOffsetArray,
                BlockTotals = view.ContactScanBlockArray,
                Count = pairCount,
                BlockSize = CollisionWorldView.ScanBlockSize,
            }.Schedule(scanBlocks, 1, handle);
        }

        private static JobHandle ScheduleContactCollect(
            in CollisionWorldView view,
            in CollisionConfig config,
            int pairCount,
            int outputLimit)
        {
            return new ContactCollectJob
            {
                Pairs = view.CandidatePairArray,
                BodyEntities = view.BodyEntityArray,
                BodyPoses = view.BodyPoseArray,
                BodyColliders = view.BodyColliderArray,
                BodyFilters = view.BodyFilterArray,
                BodyFlags = view.BodyFlagArray,
                Vertices = view.VertexArray,
                ContactCounts = view.ContactCountArray,
                ContactOffsets = view.ContactOffsetArray,
                Contacts = view.ContactArray,
                BodyCount = view.BodyCount,
                VertexCount = view.VertexCount,
                OutputLimit = outputLimit,
                Dimension = config.Dimension,
                SkipStaticPairs = config.SkipStaticPairs,
            }.Schedule(pairCount, 64, default);
        }

        private static JobHandle ScheduleContactFlagMark(in CollisionWorldView view, int pairCount)
        {
            return new ContactFlagMarkJob
            {
                Pairs = view.CandidatePairArray,
                ContactCounts = view.ContactCountArray,
                BodyContactFlags = view.BodyContactFlagArray,
                PairCount = pairCount,
                BodyCount = view.BodyCount,
            }.Schedule();
        }

        private static JobHandle ScheduleStateWrite(in CollisionWorldView view, JobHandle dependency)
        {
            return new ContactStateWriteJob
            {
                ChunkInfos = view.ChunkInfoArray,
                BodyContactFlags = view.BodyContactFlagArray,
            }.Schedule(view.ChunkInfoArray.Length, 8, dependency);
        }

        /// <summary>
        /// Disabled body 不进入宽相的 dense 表，故需单独推进其接触历史。
        /// 这里运行在所有 P2 Job 完成之后，且只访问被宽相排除的 chunk。
        /// </summary>
        private void AdvanceDisabledStates(SystemContext ctx)
        {
            foreach (var chunk in ctx.QueryChunks(m_DisabledStateQuery))
            {
                ChunkColumn<CollisionState> states = chunk.Write<CollisionState>();
                for (int row = 0; row < states.Count; row++)
                {
                    ref CollisionState state = ref states[row];
                    state.SetFrameContact(false);
                }
            }
        }

        private static void WarnOnDiagnostics(in CollisionWorldView view)
        {
            NativeArray<int> diagnostics = view.DiagnosticFlagArray;
            if (!diagnostics.IsCreated) return;
            if (diagnostics[CollisionWorld.DiagContactCapacityTruncated] != 0)
            {
                Debug.LogError(
                    "[Ember.Collision] 接触流形容量不足：当帧流形按 CollisionConfig.MaxContacts 被截断。");
            }
        }
    }
}
