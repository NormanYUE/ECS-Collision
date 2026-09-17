namespace Ember.Collision
{
    /// <summary>一对碰撞体跨帧接触关系的变化。</summary>
    public enum ContactEventPhase : byte
    {
        Enter = 0,
        Stay = 1,
        Exit = 2,
    }

    /// <summary>按 pair 发布的接触事件。A/B 始终按实体槽位升序规范化。</summary>
    public struct ContactEvent
    {
        public Entity A;
        public Entity B;
        public ContactEventPhase Phase;
    }

    /// <summary>CollisionWorld 内部持久化的 pair 记录，用于跨帧归并。</summary>
    public struct ContactPairRecord
    {
        public ulong Key;
        public Entity A;
        public Entity B;

        public static ContactPairRecord Create(Entity a, Entity b)
        {
            if (a.Index > b.Index)
            {
                Entity swap = a;
                a = b;
                b = swap;
            }

            return new ContactPairRecord
            {
                Key = PairKey.Pack(a, b),
                A = a,
                B = b,
            };
        }
    }
}
