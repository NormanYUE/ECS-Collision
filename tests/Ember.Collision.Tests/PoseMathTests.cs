using NUnit.Framework;
using Unity.Mathematics;

namespace Ember.Collision.Tests
{
    /// <summary>
    /// 位姿解算测试。核心断言：从矩阵解出的旋转必须与原始旋转<b>在作用效果上等价</b>
    /// （四元数 q 与 -q 表示同一旋转，故不能直接比分量，必须比较作用于向量的结果）。
    /// </summary>
    [TestFixture]
    public class PoseMathTests
    {
        private static readonly float3[] s_ProbeDirections =
        {
            new(1f, 0f, 0f),
            new(0f, 1f, 0f),
            new(0f, 0f, 1f),
            new(1f, 2f, 3f),
            new(-4f, 0.5f, 2f),
        };

        /// <summary>
        /// 独立推导的四元数 → 旋转矩阵（按列填充）。
        /// 刻意不使用 Unity 自带的 quaternion→float3x3 转换，否则约定错误会被
        /// 「转换来回抵消」掩盖：这里用最原始的定义作为独立参照。
        /// </summary>
        private static float4x4 ToRotationMatrix(quaternion q)
        {
            float3 c0 = math.mul(q, new float3(1f, 0f, 0f));
            float3 c1 = math.mul(q, new float3(0f, 1f, 0f));
            float3 c2 = math.mul(q, new float3(0f, 0f, 1f));
            return new float4x4(
                new float4(c0, 0f), new float4(c1, 0f), new float4(c2, 0f), new float4(0f, 0f, 0f, 1f));
        }

        private static void AssertRotationEquivalent(quaternion expected, quaternion actual, string context)
        {
            foreach (float3 direction in s_ProbeDirections)
            {
                float3 a = math.mul(expected, direction);
                float3 b = math.mul(actual, direction);
                Assert.That(math.distance(a, b), Is.LessThan(1e-4f),
                    $"{context}: 方向 {direction} 在期望旋转下为 {a}，实际为 {b}");
            }
        }

        [Test]
        public void Identity_ExtractsIdentity()
        {
            var pose = PoseMath.FromMatrix(float4x4.identity);

            Assert.That(pose.Position, Is.EqualTo(float3.zero));
            Assert.That(pose.Scale, Is.EqualTo(1f).Within(1e-6f));
            AssertRotationEquivalent(quaternion.identity, pose.Rotation, "identity");
        }

        [Test]
        public void Translation_ExtractsPosition()
        {
            var pose = PoseMath.FromMatrix(float4x4.Translate(new float3(3f, -4f, 5f)));

            Assert.That(pose.Position, Is.EqualTo(new float3(3f, -4f, 5f)));
            AssertRotationEquivalent(quaternion.identity, pose.Rotation, "translation");
            Assert.That(pose.Scale, Is.EqualTo(1f).Within(1e-6f));
        }

        [Test]
        public void UniformScale_ExtractsScaleAndKeepsUnitRotation()
        {
            var pose = PoseMath.FromMatrix(float4x4.Scale(new float3(2.5f)));

            Assert.That(pose.Scale, Is.EqualTo(2.5f).Within(1e-4f));
            AssertRotationEquivalent(quaternion.identity, pose.Rotation, "scaled identity");
            Assert.That(math.length(pose.Rotation.value), Is.EqualTo(1f).Within(1e-4f),
                "解出的四元数必须是单位长度");
        }

        [Test]
        public void RotationOnly_IsExtractedExactly()
        {
            var expected = quaternion.AxisAngle(math.normalize(new float3(1f, 1f, 0f)), math.radians(50f));
            var pose = PoseMath.FromMatrix(ToRotationMatrix(expected));

            AssertRotationEquivalent(expected, pose.Rotation, "rotation only");
        }

        [Test]
        public void FullAffine_ExtractsAllComponents()
        {
            var expectedRotation = quaternion.AxisAngle(math.normalize(new float3(0.3f, -1f, 0.7f)), math.radians(123f));
            var expectedPosition = new float3(7f, -2f, 4.5f);
            const float expectedScale = 3.25f;

            var matrix = math.mul(
                float4x4.Translate(expectedPosition),
                math.mul(ToRotationMatrix(expectedRotation), float4x4.Scale(new float3(expectedScale))));

            var pose = PoseMath.FromMatrix(matrix);

            Assert.That(math.distance(pose.Position, expectedPosition), Is.LessThan(1e-4f));
            Assert.That(pose.Scale, Is.EqualTo(expectedScale).Within(1e-4f));
            AssertRotationEquivalent(expectedRotation, pose.Rotation, "full affine");
        }

        [Test]
        public void AxisAngleSweep_AllExtractedCorrectly()
        {
            // 覆盖四个 Shepperd 分支：trace 为负且分别以 m00 / m11 / m22 为最大对角元。
            float3[] axes =
            {
                new(1f, 0f, 0f),
                new(0f, 1f, 0f),
                new(0f, 0f, 1f),
                math.normalize(new float3(1f, 1f, 1f)),
                math.normalize(new float3(-1f, 1f, 0.5f)),
            };
            float[] angles = { 10f, 90f, 179f, 181f, 270f, 359f };

            foreach (float3 axis in axes)
            {
                foreach (float angle in angles)
                {
                    var expected = quaternion.AxisAngle(axis, math.radians(angle));
                    var pose = PoseMath.FromMatrix(ToRotationMatrix(expected));

                    AssertRotationEquivalent(expected, pose.Rotation, $"axis={axis} angle={angle}");
                }
            }
        }

        [Test]
        public void NegativeScale_DoesNotProduceReflection()
        {
            // 镜像矩阵（负缩放）必须解出正缩放 + 纯旋转，否则窄相会得到翻转的法线。
            var mirrored = float4x4.Scale(new float3(-1f, 1f, 1f));
            var pose = PoseMath.FromMatrix(mirrored);

            Assert.That(pose.Scale, Is.GreaterThan(0f), "缩放必须为正");
            Assert.That(math.length(pose.Rotation.value), Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void DegenerateMatrix_DoesNotProduceNaN()
        {
            var pose = PoseMath.FromMatrix(default);

            Assert.That(float.IsNaN(pose.Position.x), Is.False);
            Assert.That(float.IsNaN(pose.Rotation.value.x), Is.False);
            Assert.That(pose.Scale, Is.EqualTo(1f).Within(1e-6f));
            AssertRotationEquivalent(quaternion.identity, pose.Rotation, "degenerate");
        }

        [Test]
        public void BodyPoseTransform_Roundtrips()
        {
            var pose = new BodyPose
            {
                Position = new float3(1f, 2f, 3f),
                Rotation = quaternion.AxisAngle(math.normalize(new float3(0f, 1f, 1f)), math.radians(80f)),
                Scale = 2f,
            };

            float3 local = new(0.5f, -1.5f, 2f);
            float3 world = pose.TransformPoint(local);
            float3 backToLocal = pose.InverseTransformPoint(world);

            Assert.That(math.distance(backToLocal, local), Is.LessThan(1e-3f));
        }

        [Test]
        public void BodyPoseTransformDirection_IgnoresScaleAndPosition()
        {
            var pose = new BodyPose
            {
                Position = new float3(100f, 0f, 0f),
                Rotation = quaternion.AxisAngle(new float3(0f, 0f, 1f), math.radians(90f)),
                Scale = 5f,
            };

            float3 direction = pose.TransformDirection(new float3(1f, 0f, 0f));

            Assert.That(math.distance(direction, new float3(0f, 1f, 0f)), Is.LessThan(1e-4f));
        }
    }
}
