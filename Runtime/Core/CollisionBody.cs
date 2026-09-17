namespace Ember.Collision
{
    /// <summary>
    /// 碰撞体运行时状态（Job 可读的纯数据镜像）。
    ///
    /// 存在理由：Ember 的 <c>Static</c> / <c>Disabled</c> 是 <c>ITagComponent</c>，
    /// Tag 只占 Archetype 掩码、<b>没有列</b>，因此 Burst Job 无法读取。
    /// 本组件由 <c>CollisionSetupSystem</c> 从 Tag 同步而来，让宽相 Job 能在
    /// 不离开 Burst 的前提下完成「静态-静态跳过」「禁用跳过」等过滤。
    /// </summary>
    public struct CollisionBody : IDataComponent
    {
        /// <summary>实体句柄（Job 内无法经 <c>Chunk.GetEntity</c> 取得，故随列携带）。</summary>
        public Entity Self;

        /// <summary>状态标志位。</summary>
        public byte Flags;

        /// <summary>静态体位：与 <c>Ember.Core.Static</c> Tag 同步。</summary>
        public const byte StaticBit = 1;

        /// <summary>启用位：与 <c>Ember.Core.Disabled</c> Tag 同步（取反）。</summary>
        public const byte EnabledBit = 2;

        /// <summary>本帧是否参与碰撞检测。</summary>
        public const byte ActiveBit = 4;

        /// <summary>是否为静态体。</summary>
        public bool IsStatic
        {
            readonly get => (Flags & StaticBit) != 0;
            set => Flags = (byte)(value ? Flags | StaticBit : Flags & ~StaticBit);
        }

        /// <summary>是否启用。</summary>
        public readonly bool IsEnabled => (Flags & EnabledBit) != 0;

        /// <summary>本帧是否参与检测（同时具备启用位和活动位）。</summary>
        public readonly bool IsActive =>
            (Flags & (EnabledBit | ActiveBit)) == (EnabledBit | ActiveBit);

        /// <summary>设置活动位；激活时同时确保启用，停用时保留启用配置以便重新激活。</summary>
        public void SetActive(bool active)
        {
            if (active) Flags |= EnabledBit | ActiveBit;
            else Flags = (byte)(Flags & ~ActiveBit);
        }
    }
}
