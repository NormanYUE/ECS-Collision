namespace Ember.Collision
{
    /// <summary>
    /// 碰撞接触状态（每帧由窄相 Job 写入）。bit0 = 本帧存在接触，bit1 = 上一帧存在接触。
    ///
    /// 采用与 <c>Ember.Core.VisibilityState</c> 完全相同的「位移 + 双位」范式：
    /// 任何系统都能仅从组件数据推导出接触的进入 / 离开边沿，
    /// 无需历史缓存、无需结构变更。
    /// </summary>
    public struct CollisionState : IDataComponent
    {
        /// <summary>位标志。bit0 = 本帧有接触；bit1 = 上一帧有接触。</summary>
        public byte Flags;

        /// <summary>本帧有接触位。</summary>
        public const byte ContactBit = 1;

        /// <summary>上一帧有接触位。</summary>
        public const byte WasContactBit = 2;

        /// <summary>本帧是否有接触。</summary>
        public bool HasContact
        {
            readonly get => (Flags & ContactBit) != 0;
            set => Flags = (byte)(value ? Flags | ContactBit : Flags & ~ContactBit);
        }

        /// <summary>上一帧是否有接触。</summary>
        public bool HadContact
        {
            readonly get => (Flags & WasContactBit) != 0;
            set => Flags = (byte)(value ? Flags | WasContactBit : Flags & ~WasContactBit);
        }

        /// <summary>本帧刚进入接触。</summary>
        public readonly bool EnteredContact => HasContact && !HadContact;

        /// <summary>本帧刚离开接触。</summary>
        public readonly bool ExitedContact => !HasContact && HadContact;

        /// <summary>把当前帧状态移入历史位（每帧接触写入开始前调用一次）。</summary>
        public void ShiftHistory()
        {
            byte current = (byte)(Flags & ContactBit);
            Flags = (byte)((current != 0 ? WasContactBit : 0) | (Flags & ContactBit));
        }

        /// <summary>推进一帧，并写入本帧是否有接触。</summary>
        public void SetFrameContact(bool hasContact)
        {
            ShiftHistory();
            HasContact = hasContact;
        }
    }
}
