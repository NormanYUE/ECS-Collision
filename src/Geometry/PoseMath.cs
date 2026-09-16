using Unity.Mathematics;

namespace Ember.Collision
{
    /// <summary>
    /// 位姿解算（工具类，静态豁免，纯函数，Burst 可编译）。
    /// </summary>
    public static class PoseMath
    {
        private const float k_MinScale = 1e-8f;

        /// <summary>
        /// 从 <c>LocalToWorld</c> 仿射矩阵解出位置 / 旋转 / 等比缩放。
        /// 旋转部分先逐轴归一化再转四元数，因此带缩放的矩阵也能得到正确的单位旋转。
        /// 退化轴（长度趋零）回退到单位基向量，保证不产生 NaN。
        /// </summary>
        public static BodyPose FromMatrix(in float4x4 matrix)
        {
            float3 c0 = matrix.c0.xyz;
            float3 c1 = matrix.c1.xyz;
            float3 c2 = matrix.c2.xyz;

            float sx = math.length(c0);
            float sy = math.length(c1);
            float sz = math.length(c2);

            float3x3 basis = default;
            basis.c0 = sx > k_MinScale ? c0 / sx : new float3(1f, 0f, 0f);
            basis.c1 = sy > k_MinScale ? c1 / sy : new float3(0f, 1f, 0f);
            basis.c2 = sz > k_MinScale ? c2 / sz : new float3(0f, 0f, 1f);

            // 等比缩放取轴长最大值（镜像产生的负号被忽略，避免解出反射位姿）。
            float scale = math.max(sx, math.max(sy, sz));

            return new BodyPose
            {
                Position = matrix.c3.xyz,
                Rotation = FromOrthonormalBasis(basis),
                Scale = scale > k_MinScale ? scale : 1f,
            };
        }

        /// <summary>
        /// 从正交单位基（列向量）构造四元数。
        /// 采用 Shepperd 法按最大对角元分支，数值稳定，避开 trace 趋零时的除零。
        /// </summary>
        public static quaternion FromOrthonormalBasis(in float3x3 m)
        {
            // Unity.Mathematics 的 float3x3 为列主序：m.cC[R] 即行主序的 M[R][C]。
            // 下面统一转写为 mRC = M[行R][列C] 的命名，套用标准 Shepperd 公式。
            float m00 = m.c0.x, m01 = m.c1.x, m02 = m.c2.x;
            float m10 = m.c0.y, m11 = m.c1.y, m12 = m.c2.y;
            float m20 = m.c0.z, m21 = m.c1.z, m22 = m.c2.z;

            float trace = m00 + m11 + m22;
            float x, y, z, w;

            if (trace > 0f)
            {
                float s = math.sqrt(trace + 1f) * 2f;
                w = 0.25f * s;
                x = (m21 - m12) / s;
                y = (m02 - m20) / s;
                z = (m10 - m01) / s;
            }
            else if (m00 > m11 && m00 > m22)
            {
                float s = math.sqrt(1f + m00 - m11 - m22) * 2f;
                w = (m21 - m12) / s;
                x = 0.25f * s;
                y = (m01 + m10) / s;
                z = (m02 + m20) / s;
            }
            else if (m11 > m22)
            {
                float s = math.sqrt(1f + m11 - m00 - m22) * 2f;
                w = (m02 - m20) / s;
                x = (m01 + m10) / s;
                y = 0.25f * s;
                z = (m12 + m21) / s;
            }
            else
            {
                float s = math.sqrt(1f + m22 - m00 - m11) * 2f;
                w = (m10 - m01) / s;
                x = (m02 + m20) / s;
                y = (m12 + m21) / s;
                z = 0.25f * s;
            }

            return math.normalizesafe(new quaternion(x, y, z, w), quaternion.identity);
        }

        /// <summary>由轴与角度构造旋转（便捷入口）。</summary>
        public static quaternion AxisAngle(float3 axis, float radians) =>
            quaternion.AxisAngle(math.normalizesafe(axis, new float3(0f, 1f, 0f)), radians);
    }
}
