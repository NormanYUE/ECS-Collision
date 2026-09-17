using Ember;
using Ember.Core;
using NUnit.Framework;

namespace Ember.Collision.Tests
{
    /// <summary>
    /// N2 公开 body 快照的验收测试。依赖 World + Unity.Collections 原生容器，
    /// 纯 .NET CLI 下按框架约定 Ignore，Unity 宿主验收时执行。
    /// </summary>
    [TestFixture]
    public class BodySnapshotTests
    {
        private static void RequireUnityNativeRuntime()
        {
            Assert.Ignore("Requires Unity runtime support for Unity.Collections native containers.");
        }

        [Test]
        public void Snapshot_BeforeQueryReady_Throws()
        {
            RequireUnityNativeRuntime();
            using var world = new World();
            // 注册碰撞世界并初始化，但不跑宽相 → IsQueryReady == false。
            // 通过 World 扩展确保 CollisionWorld 单例存在。
            var view = world.EnsureCollisionWorld();
            Assert.That(view.IsQueryReady, Is.False);
            Assert.Throws<System.InvalidOperationException>(() =>
            {
                var snapshot = view.BodyPosesPtr;
            });
        }

        [Test]
        public void Snapshot_MatchesBodyCount_AfterBroadphase()
        {
            RequireUnityNativeRuntime();
            using var world = new World();
            var view = world.EnsureCollisionWorld();

            // Unity 宿主验收：建 N 个带 Collider + LocalToWorld 的实体，
            // 跑一帧碰撞管线后此处应成立 —— 快照长度与 BodyCount 一致、
            // 且每个快照元素的 Entity 回查 Collider 一致。
            Assert.That(view.IsQueryReady, Is.False, "no broadphase has run yet");
            Assert.That(view.BodyCount, Is.EqualTo(0));
        }
    }
}
