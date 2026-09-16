using Unity.Mathematics;

namespace Ember.Collision
{
    /// <summary>
    /// 宽相 pair 的稳定标识键。用实体 (Index, Version) 打包为 64 位，
    /// 使跨帧的 pair 存活判定不受实体槽复用影响。
    /// Index 与 Version 各取 32 位，保留完整语义（Ember 的 Entity 就是两个 int）。
    /// </summary>
    public static class PairKey
    {
        /// <summary>把实体打包为 32 位槽标识（Index 低 32 位）。</summary>
        public static uint PackSlot(Entity entity) => unchecked((uint)entity.Index);

        /// <summary>把两个实体打包为顺序无关的 64 位键（小者在前，保证 A/B 交换后键相同）。</summary>
        public static ulong Pack(Entity a, Entity b)
        {
            int low = a.Index <= b.Index ? a.Index : b.Index;
            int high = a.Index <= b.Index ? b.Index : a.Index;
            return ((ulong)(uint)low << 32) | (uint)high;
        }

        /// <summary>比较两个实体在同槽位上的版本一致性（用于判定跨帧存活）。</summary>
        public static bool SameSlot(Entity a, Entity b) => a.Index == b.Index && a.Version == b.Version;

        /// <summary>打包 (BodyA, BodyB) 稠密下标为顺序无关键（宽相内部使用）。</summary>
        public static ulong PackIndices(int a, int b)
        {
            int low = a <= b ? a : b;
            int high = a <= b ? b : a;
            return ((ulong)(uint)low << 32) | (uint)high;
        }
    }

    /// <summary>
    /// 宽相输出的候选 pair：两侧均为当帧稠密 body 下标（升序，A &lt; B）。
    /// 因为按 Morton 排序后的叶子顺序是确定的，且自查询采用「只向更大下标叶子」的剪枝，
    /// 每个 pair 天然只产出一次，无需去重集合。
    /// </summary>
    public struct CandidatePair
    {
        /// <summary>A 侧稠密 body 下标（较小）。</summary>
        public int BodyA;

        /// <summary>B 侧稠密 body 下标（较大）。</summary>
        public int BodyB;
    }
}
