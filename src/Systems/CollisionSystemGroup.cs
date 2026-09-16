using Ember.Core;

namespace Ember.Collision
{
    /// <summary>
    /// 碰撞系统组：把完整碰撞管线封装为一次注册。
    ///
    /// <code>
    /// manager.GetTicker(updateIdx).Register&lt;CollisionSystemGroup&gt;();
    /// // 若还需视锥剔除 / 空间查询，再叠加框架的空间组（顺序无关，依赖图会自动分层）：
    /// manager.GetTicker(updateIdx).Register&lt;SpatialSystemGroup&gt;();
    /// </code>
    ///
    /// 组内注册顺序即管线顺序（依赖图再按读写冲突自动分层）：
    /// ① 补齐组件 → ② 宽相（采集 / 排序 / BVH / 候选 pair）
    /// → ③ 窄相（精确判定 / 流形计数 / 前缀和 / 状态与流形写入）。
    ///
    /// 与 Ember.Core 的 <c>SpatialSystemGroup</c> 的关系：
    /// 本组的采集 Job 会写 <c>BoundingVolume</c>，框架的 <c>WorldBoundsSystem</c>
    /// 随即据此算出 <c>WorldBounds</c>；两个组可以同时注册，
    /// 依赖图会把 <c>WorldBoundsSystem</c> 排在采集之后、剔除之前。
    /// </summary>
    public sealed class CollisionSystemGroup : SystemGroup
    {
        // 框架将基类无参 Configure 标记 Obsolete 以强制显式重写（未来大版本改为 abstract）；
        // 重写 Obsolete 成员触发 CS0672，此处属框架过渡期的预期用法，抑制之。
#pragma warning disable CS0672
        public override void Configure(SystemTicker ticker)
#pragma warning restore CS0672
        {
            ticker.Register<CollisionSetupSystem>();
            ticker.Register<CollisionBroadphaseSystem>();
            ticker.Register<CollisionNarrowphaseSystem>();
            ticker.Register<CollisionContactEventSystem>();
        }
    }
}
