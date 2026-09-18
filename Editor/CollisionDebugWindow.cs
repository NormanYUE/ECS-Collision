using Ember;
using UnityEditor;
using UnityEngine;

namespace Ember.Collision.Editor
{
    /// <summary>
    /// 碰撞调试窗口：开关场景视图的各图层，并显示宽相 / 窄相的计数与诊断位。
    ///
    /// 计数是判断「是没配上对还是窄相没解出来」的第一手依据 ——
    /// body 数与 pair 数对不上说明宽相没覆盖，pair 有而 contact 为 0 说明窄相被过滤掉了。
    /// </summary>
    public class CollisionDebugWindow : EditorWindow
    {
        private Vector2 m_Scroll;

        [MenuItem("Ember/Collision/调试窗口", priority = 0)]
        private static void Open()
        {
            var window = GetWindow<CollisionDebugWindow>("Ember 碰撞调试");
            window.minSize = new Vector2(300f, 260f);
        }

        private void OnInspectorUpdate()
        {
            Repaint();
        }

        private void OnGUI()
        {
            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);

            DrawLayerToggles();
            EditorGUILayout.Space(6f);
            DrawStats();

            EditorGUILayout.EndScrollView();
        }

        private static void DrawLayerToggles()
        {
            EditorGUILayout.LabelField("图层", EditorStyles.boldLabel);
            CollisionDebugSettings.DrawShapes = EditorGUILayout.ToggleLeft(
                "碰撞体形状（按 body 池的 pose + collider）", CollisionDebugSettings.DrawShapes);
            CollisionDebugSettings.DrawBvh = EditorGUILayout.ToggleLeft(
                "BVH 内部节点包围盒", CollisionDebugSettings.DrawBvh);
            CollisionDebugSettings.DrawPairs = EditorGUILayout.ToggleLeft(
                "宽相候选 pair 连线", CollisionDebugSettings.DrawPairs);
            CollisionDebugSettings.DrawContacts = EditorGUILayout.ToggleLeft(
                "接触点与法线", CollisionDebugSettings.DrawContacts);

            EditorGUILayout.Space(4f);
            CollisionDebugSettings.DrawLimit = EditorGUILayout.IntField(
                "形状/连线/接触绘制上限", CollisionDebugSettings.DrawLimit);
            CollisionDebugSettings.BvhLimit = EditorGUILayout.IntField(
                "BVH 节点绘制上限", CollisionDebugSettings.BvhLimit);
            CollisionDebugSettings.NormalLength = EditorGUILayout.Slider(
                "法线长度（米）", CollisionDebugSettings.NormalLength, 0.05f, 2f);
        }

        private static void DrawStats()
        {
            EditorGUILayout.LabelField("运行时统计", EditorStyles.boldLabel);

            if (!Application.isPlaying) {
                EditorGUILayout.HelpBox("碰撞世界只在 Play 模式存在。", MessageType.Info);
                return;
            }

            World world = CollisionDebugSettings.ResolveManager()?.World;
            if (world is not { IsDisposed: false } || !world.TryGetCollisionWorld(out CollisionWorldView collision)) {
                EditorGUILayout.HelpBox("没有碰撞世界。", MessageType.Warning);
                return;
            }

            if (!collision.IsQueryReady) {
                EditorGUILayout.HelpBox("宽相尚未发布（IsQueryReady = false）。", MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField("碰撞体数", collision.BodyCount.ToString());
            EditorGUILayout.LabelField("分块数", collision.ChunkCount.ToString());
            EditorGUILayout.LabelField("BVH 内部节点", collision.InternalNodeCount.ToString());
            EditorGUILayout.LabelField("顶点池", collision.VertexCount.ToString());

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("候选 pair", $"{collision.CandidatePairCount} / 容量 {collision.PairCapacity}");
            EditorGUILayout.LabelField("接触流形", $"{collision.ContactCount} / 容量 {collision.ContactCapacity}");
            EditorGUILayout.LabelField("检出接触", collision.DetectedContactCount.ToString());
            EditorGUILayout.LabelField("上一帧 pair", collision.PreviousContactPairCount.ToString());
            EditorGUILayout.LabelField("接触事件", collision.ContactEventCount.ToString());

            if (collision.OverflowCount > 0) {
                EditorGUILayout.HelpBox(
                    $"容量溢出计数 {collision.OverflowCount}：候选 pair 或接触数超过了 CollisionConfig 的容量，" +
                    "部分碰撞被丢弃。调大对应容量或降低密度。",
                    MessageType.Warning);
            }
        }
    }
}
