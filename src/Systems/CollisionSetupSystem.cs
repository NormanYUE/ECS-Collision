using Ember.Core;

namespace Ember.Collision
{
    /// <summary>
    /// 碰撞组件补齐系统（串行）。
    ///
    /// 为「带 <see cref="Collider"/>」的实体自动补上碰撞管线所需的全部组件
    /// （<see cref="CollisionBody"/> / <see cref="CollisionFilter"/> /
    /// <see cref="CollisionState"/> / <c>BoundingVolume</c>），每实体一生一次。
    /// 因此使用方只需：
    /// <code>
    /// world.AddComponent(entity, Collider.Sphere(1f));
    /// world.AddComponent(entity, new LocalToWorld { Value = float4x4.identity });
    /// </code>
    /// 其余组件由系统补齐，无需手工登记。
    ///
    /// <c>BoundingVolume</c> 由本系统补上（初值为零），随后由碰撞的采集 Job 填入
    /// 形状的本地 AABB；框架既有的 <c>SpatialSetupSystem</c> 会据此再补
    /// <c>WorldBounds</c>/<c>VisibilityState</c>，于是碰撞体自动获得
    /// 视锥剔除与空间索引能力。
    ///
    /// <b>必须在 <see cref="CollisionBroadphaseSystem"/> 之前注册。</b>
    /// </summary>
    public sealed class CollisionSetupSystem : SystemBase
    {
        // 查询在 OnCreate 构造，不用字段初始化器：系统由 SystemTicker.Register 立即构造，
        // 早于 ECSManager.Start()、早于 World 构造，而 ComponentMask.With<T>() 会当场读组件注册表。
        private EntityQuery m_MissingBody;
        private EntityQuery m_MissingFilter;
        private EntityQuery m_MissingState;
        private EntityQuery m_MissingVolume;

        public override void OnCreate()
        {
            m_MissingBody = new EntityQuery(
                new ComponentMask().With<Collider>(),
                ComponentMask.Empty,
                new ComponentMask().With<CollisionBody>().With<Prefab>());

            m_MissingFilter = new EntityQuery(
                new ComponentMask().With<Collider>(),
                ComponentMask.Empty,
                new ComponentMask().With<CollisionFilter>().With<Prefab>());

            m_MissingState = new EntityQuery(
                new ComponentMask().With<Collider>(),
                ComponentMask.Empty,
                new ComponentMask().With<CollisionState>().With<Prefab>());

            m_MissingVolume = new EntityQuery(
                new ComponentMask().With<Collider>().With<LocalToWorld>(),
                ComponentMask.Empty,
                new ComponentMask().With<BoundingVolume>().With<Prefab>());
        }

        protected override void DeclareAccess(AccessBuilder access) => access
            .Read<Collider>()
            .Read<LocalToWorld>()
            .Write<CollisionBody>()
            .Write<CollisionFilter>()
            .Write<CollisionState>()
            .Write<BoundingVolume>()
            .StructuralChanges();

        protected override void OnTick(SystemContext ctx)
        {
            // 结构性变更一律走 ECB：由 ticker 在 tick 成功后统一回放，
            // 避免在查询迭代期间修改 Archetype。
            foreach (var chunk in ctx.QueryChunks(m_MissingBody))
            {
                for (int row = 0; row < chunk.Count; row++)
                {
                    Entity entity = chunk.EntityAt(row);
                    ctx.ECB.AddComponent(entity, new CollisionBody
                    {
                        Self = entity,
                        Flags = (byte)(CollisionBody.EnabledBit | CollisionBody.ActiveBit),
                    });
                }
            }

            foreach (var chunk in ctx.QueryChunks(m_MissingFilter))
            {
                for (int row = 0; row < chunk.Count; row++)
                    ctx.ECB.AddComponent(chunk.EntityAt(row), CollisionFilter.Default);
            }

            foreach (var chunk in ctx.QueryChunks(m_MissingState))
            {
                for (int row = 0; row < chunk.Count; row++)
                    ctx.ECB.AddComponent(chunk.EntityAt(row), default(CollisionState));
            }

            foreach (var chunk in ctx.QueryChunks(m_MissingVolume))
            {
                for (int row = 0; row < chunk.Count; row++)
                    ctx.ECB.AddComponent(chunk.EntityAt(row), default(BoundingVolume));
            }
        }
    }
}
