using Unity.Mathematics;

namespace Ember.Collision
{
    /// <summary>
    /// 接触流形（最多 4 个接触点，定长 blittable，行业标准容量）。
    /// 由窄相写入 <c>CollisionWorld</c> 的托管缓冲，不落组件——
    /// 避免每帧变化的接触数据污染 Archetype 布局与迁移成本。
    /// </summary>
    public struct ContactManifold
    {
        /// <summary>接触点容量。</summary>
        public const int Capacity = 4;

        /// <summary>A 侧实体。</summary>
        public Entity A;

        /// <summary>B 侧实体。</summary>
        public Entity B;

        /// <summary>单位法线，方向从 A 指向 B。</summary>
        public float3 Normal;

        /// <summary>接触点数（1..4）。</summary>
        public int Count;

        /// <summary>接触点 0。</summary>
        public ContactPoint P0;

        /// <summary>接触点 1。</summary>
        public ContactPoint P1;

        /// <summary>接触点 2。</summary>
        public ContactPoint P2;

        /// <summary>接触点 3。</summary>
        public ContactPoint P3;

        /// <summary>按索引读取接触点。</summary>
        public readonly ContactPoint GetPoint(int index)
        {
            switch (index)
            {
                case 0: return P0;
                case 1: return P1;
                case 2: return P2;
                default: return P3;
            }
        }

        /// <summary>按索引写入接触点。</summary>
        public void SetPoint(int index, in ContactPoint point)
        {
            switch (index)
            {
                case 0: P0 = point; break;
                case 1: P1 = point; break;
                case 2: P2 = point; break;
                default: P3 = point; break;
            }
        }

        /// <summary>追加接触点，超出容量时静默丢弃并返回 false。</summary>
        public bool TryAdd(in ContactPoint point)
        {
            if (Count >= Capacity) return false;
            SetPoint(Count, point);
            Count++;
            return true;
        }

        /// <summary>最浅穿透（负分离）深度；全为分离时返回 0。</summary>
        public readonly float Penetration
        {
            get
            {
                float worst = 0f;
                for (int i = 0; i < Count; i++)
                {
                    float separation = GetPoint(i).Separation;
                    if (separation < worst) worst = separation;
                }
                return -worst;
            }
        }

        /// <summary>是否存在真实接触（至少一点穿透或贴合）。</summary>
        public readonly bool HasContact
        {
            get
            {
                for (int i = 0; i < Count; i++)
                    if (GetPoint(i).Separation <= 0f) return true;
                return false;
            }
        }
    }
}
