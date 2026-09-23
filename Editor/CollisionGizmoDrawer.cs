using Ember;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace Ember.Collision.Editor
{
    /// <summary>
    /// 场景视图里的碰撞可视化：碰撞体形状、BVH 内部节点包围盒、宽相候选 pair 连线、接触点与法线。
    ///
    /// 数据全部直接读运行中世界的裸指针（<c>BodyPosesPtr</c> / <c>BodyCollidersPtr</c> /
    /// <c>BvhNodePtr</c> / <c>CandidatePairPtr</c> / <c>ContactPtr</c>），
    /// 与作业侧读的是同一份内存，因此画出来的就是求解用的那份，不存在「调试副本不一致」。
    ///
    /// 只在 Play 模式有效：碰撞世界只在运行时存在。所有读取都要求 <c>IsQueryReady</c>，
    /// 否则指针背后的 buffer 还没发布。
    /// </summary>
    [InitializeOnLoad]
    public static class CollisionGizmoDrawer
    {
        private static readonly Color ShapeColor = new(0.4f, 0.85f, 1f, 0.9f);
        private static readonly Color ContactShapeColor = new(1f, 0.24f, 0.2f, 1f);
        private static readonly Color PairColor = new(1f, 1f, 1f, 0.18f);
        private static readonly Color ContactPairColor = new(1f, 0.32f, 0.26f, 0.95f);
        private static readonly Color ContactColor = new(1f, 0.4f, 0.3f, 1f);
        private static readonly Color NormalColor = new(1f, 0.9f, 0.2f, 1f);

        static CollisionGizmoDrawer()
        {
            SceneView.duringSceneGui += OnSceneGui;
        }

        private static void OnSceneGui(SceneView view)
        {
            // 只在 Repaint 事件绘制：duringSceneGui 对 Layout/鼠标事件同样触发，
            // 此时发 GL（HandleCap 硬编码 Repaint 不区分事件）会污染 Scene 视图渲染（花屏）。
            if (Event.current.type != EventType.Repaint) return;
            if (!Application.isPlaying) return;
            if (!CollisionDebugSettings.DrawShapes && !CollisionDebugSettings.DrawBvh
                && !CollisionDebugSettings.DrawContacts && !CollisionDebugSettings.DrawPairs) return;

            World world = CollisionDebugSettings.ResolveManager()?.World;
            if (world is not { IsDisposed: false }) return;
            if (!world.TryGetCollisionWorld(out CollisionWorldView collision) || !collision.IsQueryReady) return;

            if (CollisionDebugSettings.DrawBvh) DrawBvh(collision);
            if (CollisionDebugSettings.DrawShapes) DrawShapes(collision);
            if (CollisionDebugSettings.DrawPairs) DrawPairs(collision);
            if (CollisionDebugSettings.DrawContacts) DrawContacts(collision);
        }

        #region 形状

        private static unsafe void DrawShapes(in CollisionWorldView collision)
        {
            int bodyCount = collision.BodyCount;
            if (bodyCount <= 0) return;

            var poses = (BodyPose*)collision.BodyPosesPtr;
            var colliders = (Collider*)collision.BodyCollidersPtr;
            if (poses == null || colliders == null) return;

            // 本帧接触标志：非 0 的体画红色（与实体上 CollisionState.HasContact 同源）。
            byte* contactFlags = null;
            if (CollisionDebugSettings.HighlightContacts)
                contactFlags = (byte*)collision.BodyContactFlagsPtr;

            int limit = math.min(bodyCount, CollisionDebugSettings.DrawLimit);
            // 体按 Morton 排序，接触标志在数组里成簸出现；
            // 按「状态变化才换色」遍历，能把 Handles.color 切换压到接近簇数。
            bool touching = false;
            Handles.color = ShapeColor;

            for (int i = 0; i < limit; i++) {
                bool nowTouching = contactFlags != null && contactFlags[i] != 0;
                if (nowTouching != touching) {
                    touching = nowTouching;
                    Handles.color = touching ? ContactShapeColor : ShapeColor;
                }

                DrawShape(collision, in poses[i], in colliders[i]);
            }
        }

        private static unsafe void DrawShape(in CollisionWorldView collision, in BodyPose pose, in Collider collider)
        {
            ShapeParams parameters = collider.Params;
            float3 center = pose.TransformPoint(parameters.Center);

            // 池化槽位可能带未初始化 / NaN 数据：一条坏线会污染整个 Scene 绘制批次，逐条丢弃。
            if (!IsFinite(center) || !IsFinite(parameters.Center) || !IsFinite(parameters.Extents)
                || !float.IsFinite(parameters.Radius) || !float.IsFinite(parameters.HalfHeight)
                || !IsFinite(parameters.CapsuleAxis)) return;

            // Collider 参数存的是「本地单位尺寸」，真正大小由 LocalToWorld 解出的等比缩放给出
            // （窄相同样按 Params * Pose.Scale 求解）。不乘就会在带缩放的场景里画出
            // 比真实碰撞体更大的形状——注意 pose.TransformPoint 已经带缩放，
            // 而 TransformDirection 不带，所以下面每处缩放都显式写出。
            float scale = float.IsFinite(pose.Scale) && pose.Scale > 0f ? pose.Scale : 1f;

            switch (collider.Type) {
                case ShapeType.Circle:
                    Handles.DrawWireDisc(ToVector3(center), Vector3.forward, parameters.Radius * scale);
                    break;

                case ShapeType.Box2D:
                    DrawBox2D(pose, center, parameters.Extents, scale);
                    break;

                case ShapeType.Capsule2D:
                    DrawCapsule(pose, center, parameters, scale);
                    break;

                case ShapeType.Polygon2D:
                    // 顶点池存的是本地单位顶点，TransformPoint 已含缩放，无需显式乘。
                    DrawPolygon(collision, pose, center, parameters);
                    break;

                case ShapeType.Sphere:
                    Handles.DrawWireDisc(ToVector3(center), Vector3.forward, parameters.Radius * scale);
                    Handles.DrawWireDisc(ToVector3(center), Vector3.up, parameters.Radius * scale);
                    Handles.DrawWireDisc(ToVector3(center), Vector3.right, parameters.Radius * scale);
                    break;

                case ShapeType.Box:
                    DrawBox3D(pose, center, parameters.Extents, scale);
                    break;

                case ShapeType.Capsule:
                    DrawCapsule3D(pose, center, parameters, scale);
                    break;
            }
        }

        private static void DrawBox2D(in BodyPose pose, float3 center, float3 extents, float scale)
        {
            // center 已经是世界坐标，绝不能再过一次 TransformPoint（那是本地→世界的变换，
            // 会把它当本地坐标再变换一次，位置完全错掉）。只旋转并缩放四个角偏移量。
            var corners = new Vector3[4];
            for (int i = 0; i < 4; i++) {
                float sx = (i & 1) == 0 ? -extents.x : extents.x;
                float sy = (i & 2) == 0 ? -extents.y : extents.y;
                float3 world = center + math.mul(pose.Rotation, new float3(sx, sy, 0f) * scale);
                corners[i] = ToVector3(world);
            }

            for (int i = 0; i < 4; i++) {
                Handles.DrawLine(corners[i], corners[(i + 1) % 4]);
            }
        }

        private static void DrawBox3D(in BodyPose pose, float3 center, float3 extents, float scale)
        {
            // 用旋转后的轴向拼 12 条棱：Handles.DrawWireCube 只认轴对齐。
            float3 axisX = pose.TransformDirection(new float3(extents.x, 0f, 0f) * scale);
            float3 axisY = pose.TransformDirection(new float3(0f, extents.y, 0f) * scale);
            float3 axisZ = pose.TransformDirection(new float3(0f, 0f, extents.z) * scale);

            for (int i = 0; i < 4; i++) {
                float sx = (i & 1) == 0 ? -1f : 1f;
                float sy = (i & 2) == 0 ? -1f : 1f;
                float3 offset = axisX * sx + axisY * sy;
                Handles.DrawLine(ToVector3(center + offset - axisZ), ToVector3(center + offset + axisZ));
                Handles.DrawLine(ToVector3(center - axisZ + axisX * sx - axisY), ToVector3(center - axisZ + axisX * sx + axisY));
                Handles.DrawLine(ToVector3(center - axisZ - axisX + axisY * sy), ToVector3(center - axisZ + axisX + axisY * sy));
            }
        }

        private static void DrawCapsule(in BodyPose pose, float3 center, in ShapeParams parameters, float scale)
        {
            float radius = parameters.Radius * scale;
            float2 axis = AxisVector2D(parameters.Axis);
            float3 offset = pose.TransformDirection(new float3(axis.x, axis.y, 0f) * (parameters.HalfHeight * scale));
            Vector3 a = ToVector3(center - offset);
            Vector3 b = ToVector3(center + offset);

            Handles.DrawWireDisc(a, Vector3.forward, radius);
            Handles.DrawWireDisc(b, Vector3.forward, radius);

            // 两侧外公切线：侧向量同样要过 pose 的旋转，否则非零旋转时切线画歪。
            float3 side = math.mul(pose.Rotation, new float3(-axis.y, axis.x, 0f)) * radius;
            Handles.DrawLine(a + ToVector3(side), b + ToVector3(side));
            Handles.DrawLine(a - ToVector3(side), b - ToVector3(side));
        }

        private static void DrawCapsule3D(in BodyPose pose, float3 center, in ShapeParams parameters, float scale)
        {
            float radius = parameters.Radius * scale;
            float3 axis = parameters.CapsuleAxis;
            float3 offset = pose.TransformDirection(axis * (parameters.HalfHeight * scale));
            Handles.DrawWireDisc(ToVector3(center - offset), axis, radius);
            Handles.DrawWireDisc(ToVector3(center + offset), axis, radius);

            float3 side = math.mul(pose.Rotation, math.normalizesafe(math.cross(axis, new float3(0f, 0f, 1f)), new float3(1f, 0f, 0f))) * radius;
            Handles.DrawLine(ToVector3(center - offset + side), ToVector3(center + offset + side));
            Handles.DrawLine(ToVector3(center - offset - side), ToVector3(center + offset - side));
        }

        private static unsafe void DrawPolygon(
            in CollisionWorldView collision, in BodyPose pose, float3 center, in ShapeParams parameters)
        {
            if (parameters.VertexCount < 3) return;

            var vertices = (float3*)collision.VertexPoolPtr;
            if (vertices == null) return;

            int end = parameters.VertexStart + parameters.VertexCount;
            if (end > collision.VertexCount) return;

            int count = parameters.VertexCount;
            for (int i = 0; i < count; i++) {
                float3 localA = vertices[parameters.VertexStart + i];
                float3 localB = vertices[parameters.VertexStart + (i + 1) % count];
                Handles.DrawLine(
                    ToVector3(pose.TransformPoint(localA)),
                    ToVector3(pose.TransformPoint(localB)));
            }
        }

        private static float2 AxisVector2D(byte axis) => axis == 0 ? new float2(1f, 0f) : new float2(0f, 1f);

        #endregion

        #region BVH / pair / 接触

        private static unsafe void DrawBvh(in CollisionWorldView collision)
        {
            var nodes = (BvhNode*)collision.BvhNodePtr;
            if (nodes == null) return;

            int count = collision.InternalNodeCount;
            if (count <= 0) return;

            int limit = math.min(count, CollisionDebugSettings.BvhLimit);
            for (int i = 0; i < limit; i++) {
                ref BvhNode node = ref nodes[i];
                // 层次越深画得越淡：一眼看出树的分布，而不是一团线。
                float depth = math.clamp(node.Depth / 12f, 0f, 1f);
                Handles.color = new Color(0.6f, 0.8f, 1f, 0.5f - depth * 0.35f);
                DrawAabb(node.Bounds);
            }
        }

        private static unsafe void DrawPairs(in CollisionWorldView collision)
        {
            int pairCount = collision.CandidatePairCount;
            if (pairCount <= 0) return;

            var pairs = (CandidatePair*)collision.CandidatePairPtr;
            var poses = (BodyPose*)collision.BodyPosesPtr;
            if (pairs == null || poses == null) return;

            // 每个候选 pair 的流形数：> 0 表示这是窄相确认的真实接触，画红。
            int* contactCounts = null;
            if (CollisionDebugSettings.HighlightContacts)
                contactCounts = (int*)collision.PairContactCountPtr;

            int bodyCount = collision.BodyCount;
            int limit = math.min(pairCount, CollisionDebugSettings.DrawLimit);
            bool touching = false;
            Handles.color = PairColor;

            for (int i = 0; i < limit; i++) {
                CandidatePair pair = pairs[i];
                if (pair.BodyA < 0 || pair.BodyB < 0 || pair.BodyA >= bodyCount || pair.BodyB >= bodyCount) continue;

                float3 posA = poses[pair.BodyA].Position;
                float3 posB = poses[pair.BodyB].Position;
                if (!IsFinite(posA) || !IsFinite(posB)) continue;

                bool nowTouching = contactCounts != null && contactCounts[i] > 0;
                if (nowTouching != touching) {
                    touching = nowTouching;
                    Handles.color = touching ? ContactPairColor : PairColor;
                }

                Handles.DrawLine(ToVector3(posA), ToVector3(posB));
            }
        }

        private static unsafe void DrawContacts(in CollisionWorldView collision)
        {
            int count = collision.ContactCount;
            if (count <= 0) return;

            var manifolds = (ContactManifold*)collision.ContactPtr;
            if (manifolds == null) return;

            int limit = math.min(count, CollisionDebugSettings.DrawLimit);
            float normalLength = CollisionDebugSettings.NormalLength;

            for (int i = 0; i < limit; i++) {
                ref ContactManifold manifold = ref manifolds[i];
                if (manifold.Count <= 0) continue;

                float3 normal = math.normalizesafe(manifold.Normal);
                if (!IsFinite(normal)) continue;
                for (int p = 0; p < manifold.Count; p++) {
                    ContactPoint point = manifold.GetPoint(p);
                    if (!IsFinite(point.Position)) continue;
                    var origin = ToVector3(point.Position);

                    Handles.color = ContactColor;
                    Handles.SphereHandleCap(0, origin, Quaternion.identity, 0.06f, EventType.Repaint);

                    Handles.color = NormalColor;
                    Handles.DrawLine(origin, origin + ToVector3(normal * normalLength));
                }
            }
        }

        private static void DrawAabb(in Aabb bounds)
        {
            float3 center = bounds.Center;
            float3 size = bounds.Extents * 2f;
            // 内部节点以 Aabb.Empty（±Infinity，Center 为 NaN）初始化后自底向上合并，
            // 未合并完的节点（或 NaN 体姿污染）入批即花屏：非有限包围盒一律不画。
            if (!IsFinite(center) || !IsFinite(size)) return;
            Handles.DrawWireCube(ToVector3(center), ToVector3(size));
        }

        private static bool IsFinite(float3 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

        #endregion

        private static Vector3 ToVector3(float3 value) => new(value.x, value.y, value.z);
    }
}
