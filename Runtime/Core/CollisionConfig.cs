using Unity.Mathematics;

namespace Ember.Collision
{
    /// <summary>
    /// 碰撞模块配置（单例）。缺失时使用内置默认值。
    /// </summary>
    public struct CollisionConfig : ISingletonComponent
    {
        /// <summary>维度模式。2D 时形状与宽相只使用两个轴。</summary>
        public CollisionDimension Dimension;

        /// <summary>宽相覆盖的世界中心（仅用于 Morton 量化范围基准）。</summary>
        public float3 BroadphaseWorldCenter;

        /// <summary>宽相覆盖的世界半范围（2D 时无效轴忽略）。</summary>
        public float3 BroadphaseWorldHalfExtent;

        /// <summary>宽相 BVH 最大深度。</summary>
        public int MaxTreeDepth;

        /// <summary>BVH 叶节点目标容量（每个叶最多聚合的碰撞体数）。</summary>
        public int LeafCapacity;

        /// <summary>单帧候选 pair 容量上限（超出时截断并计数告警）。</summary>
        public int MaxCandidatePairs;

        /// <summary>单帧接触流形容量上限（超出时截断并计数告警）。</summary>
        public int MaxContacts;

        /// <summary>P4 预留：顺序冲量迭代次数。未注册求解器前不生效。</summary>
        public int SolverIterations;

        /// <summary>P4 预留：位置修正允许的穿透余量（slop）。</summary>
        public float ContactSlop;

        /// <summary>P4 预留：穿透修正比例（Baumgarte 系数）。</summary>
        public float PositionCorrectionRate;

        /// <summary>P4 预留：休眠线性速度阈值。</summary>
        public float SleepLinearThreshold;

        /// <summary>P4 预留：进入休眠所需的静止时长（秒）。</summary>
        public float SleepTimeThreshold;

        /// <summary>启用静态体互相跳过（海量静态场景的关键优化）。</summary>
        public bool SkipStaticPairs;

        /// <summary>
        /// 宽相生成候选 pair 时跳过「未参与碰撞」的体（Enabled / Active 位不全，
        /// 例如已休眠 / 待重生的单位）。
        ///
        /// 这些 pair 无论如何都会被窄相 <c>IsPairEligible</c> 丢掉，提前跳过只是省掉
        /// 白做的遍历、计数与散布。判定用的是**与窄相完全相同的那组位**，
        /// 因此不改变任何接触结果（包括 <c>CollisionState</c> 的边沿），只是少做无用功。
        /// </summary>
        public bool SkipInactivePairs;

        /// <summary>内置默认值：3D、四叉/八叉范围 ±1000、跳过静态-静态。</summary>
        public static CollisionConfig Default => new()
        {
            Dimension = CollisionDimension.XYZ,
            BroadphaseWorldCenter = float3.zero,
            BroadphaseWorldHalfExtent = new float3(1000f),
            MaxTreeDepth = 32,
            LeafCapacity = 4,
            MaxCandidatePairs = 1 << 22,
            MaxContacts = 1 << 22,
            SolverIterations = 4,
            ContactSlop = 0.005f,
            PositionCorrectionRate = 0.2f,
            SleepLinearThreshold = 0.01f,
            SleepTimeThreshold = 0.5f,
            SkipStaticPairs = true,
            SkipInactivePairs = true,
        };
    }
}
