using Unity.Mathematics;

namespace Ember.Collision
{
    /// <summary>
    /// 实体的世界位姿（位置 / 旋转 / 等比缩放）。
    /// 宽相把 <c>LocalToWorld</c> 解算成本结构存入稠密数组，
    /// 使窄相只需顺序访问一个紧凑数组，避免随机回读散射的 Chunk 列。
    ///
    /// 约定：仅支持<b>等比缩放</b>（取三轴长度的最大值）。非等比缩放的碰撞体
    /// 在窄相会按最大轴处理，属于有意的取舍——非等比缩放下的形状-形状
    /// 精确求解需要完全不同的（且昂贵得多的）算法路径。
    /// </summary>
    public struct BodyPose
    {
        /// <summary>世界位置。</summary>
        public float3 Position;

        /// <summary>世界旋转（单位四元数）。</summary>
        public quaternion Rotation;

        /// <summary>等比缩放因子。</summary>
        public float Scale;

        /// <summary>单位位姿。</summary>
        public static BodyPose Identity => new()
        {
            Position = float3.zero,
            Rotation = quaternion.identity,
            Scale = 1f,
        };

        /// <summary>把本地点变换到世界空间。</summary>
        public readonly float3 TransformPoint(float3 local) =>
            Position + math.mul(Rotation, local * Scale);

        /// <summary>把本地方向变换到世界空间（不含缩放）。</summary>
        public readonly float3 TransformDirection(float3 local) => math.mul(Rotation, local);

        /// <summary>把世界点逆变换到本地空间。</summary>
        public readonly float3 InverseTransformPoint(float3 world) =>
            math.mul(math.conjugate(Rotation), world - Position) / Scale;

        /// <summary>把世界方向逆变换到本地空间。</summary>
        public readonly float3 InverseTransformDirection(float3 world) =>
            math.mul(math.conjugate(Rotation), world);
    }
}
