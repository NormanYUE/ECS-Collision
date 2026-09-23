using Ember;
using UnityEditor;
using UnityEngine;

namespace Ember.Collision.Editor
{
    /// <summary>
    /// 碰撞可视化的开关与预算。状态落在 <see cref="EditorPrefs"/> 里，
    /// 调试窗口与场景绘制器共用同一份。
    /// </summary>
    public static class CollisionDebugSettings
    {
        private const string Prefix = "Ember.Collision.Debug.";

        /// <summary>碰撞体形状：按 body 池的 pose + collider 画线框。</summary>
        public static bool DrawShapes
        {
            get => EditorPrefs.GetBool(Prefix + "Shapes", true);
            set { EditorPrefs.SetBool(Prefix + "Shapes", value); Repaint(); }
        }

        /// <summary>BVH 内部节点的包围盒（层次用颜色深浅区分）。</summary>
        public static bool DrawBvh
        {
            get => EditorPrefs.GetBool(Prefix + "Bvh", false);
            set { EditorPrefs.SetBool(Prefix + "Bvh", value); Repaint(); }
        }

        /// <summary>接触点与法线。</summary>
        public static bool DrawContacts
        {
            get => EditorPrefs.GetBool(Prefix + "Contacts", true);
            set { EditorPrefs.SetBool(Prefix + "Contacts", value); Repaint(); }
        }

        /// <summary>宽相候选 pair 的连线（按 body 世界位置连一条淡线）。</summary>
        public static bool DrawPairs
        {
            get => EditorPrefs.GetBool(Prefix + "Pairs", false);
            set { EditorPrefs.SetBool(Prefix + "Pairs", value); Repaint(); }
        }

        /// <summary>形状 / 接触点 / 连线的最大绘制条数。</summary>
        public static int DrawLimit
        {
            get => EditorPrefs.GetInt(Prefix + "Limit", 2000);
            set { EditorPrefs.SetInt(Prefix + "Limit", Mathf.Max(1, value)); Repaint(); }
        }

        /// <summary>BVH 节点的最大绘制条数（树深度越大节点越多）。</summary>
        public static int BvhLimit
        {
            get => EditorPrefs.GetInt(Prefix + "BvhLimit", 4000);
            set { EditorPrefs.SetInt(Prefix + "BvhLimit", Mathf.Max(1, value)); Repaint(); }
        }

        /// <summary>法线绘制长度（米）。</summary>
        public static float NormalLength
        {
            get => EditorPrefs.GetFloat(Prefix + "NormalLength", 0.3f);
            set { EditorPrefs.SetFloat(Prefix + "NormalLength", Mathf.Max(0.01f, value)); Repaint(); }
        }

        /// <summary>
        /// 把本帧<b>真正发生接触</b>的碰撞体与候选 pair 高亮为红色。
        ///
        /// 判定来自窄相的接触标志：碰撞体读 <c>BodyContactFlagsPtr</c>，
        /// pair 读 <c>PairContactCountPtr</c>。因此红色 = 「求解实际拿到的接触」，
        /// 而不是「宽相给出的候选」。
        /// </summary>
        public static bool HighlightContacts
        {
            get => EditorPrefs.GetBool(Prefix + "HighlightContacts", true);
            set { EditorPrefs.SetBool(Prefix + "HighlightContacts", value); Repaint(); }
        }

        /// <summary>
        /// 绘制「本帧不参与碰撞」的体（阵亡 / 未启用，即 Enabled/Active 位不全）。
        ///
        /// 默认<b>关闭</b>：这些体还在 body 池里（也还在宽相 BVH 里），但窄相
        /// <c>IsPairEligible</c> 会把它们全部过滤掉，永远不会产生接触。
        /// 把它们画出来会造成「明明有碰撞体却不撞」的误导。
        ///
        /// 打开后以灰色画出，便于确认「它在池里、但没参与」。
        /// </summary>
        public static bool DrawInactiveBodies
        {
            get => EditorPrefs.GetBool(Prefix + "DrawInactive", false);
            set { EditorPrefs.SetBool(Prefix + "DrawInactive", value); Repaint(); }
        }

        /// <summary>
        /// 当前运行的 ECS 管理器。碰撞世界只存在于运行中的世界里，
        /// 因此没有业务侧注册时退回框架的 <see cref="ECSManager.Active"/>。
        /// </summary>
        public static ECSManager ResolveManager() => ECSManager.Active;

        private static void Repaint()
        {
            SceneView.RepaintAll();
        }
    }
}
