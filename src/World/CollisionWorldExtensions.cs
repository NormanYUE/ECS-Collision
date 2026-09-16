using Unity.Collections;
using Unity.Mathematics;

namespace Ember.Collision
{
    /// <summary>
    /// 碰撞世界访问扩展（扩展类，静态豁免）。
    /// 碰撞 scratch 全部由 World 托管，<b>无需 Dispose</b>——随 <c>World.Dispose</c> 自动释放。
    /// </summary>
    public static class CollisionWorldExtensions
    {
        /// <summary>
        /// 取碰撞世界操作视图。未启用时（系统组未注册 / 尚未运行一帧）抛异常——
        /// 须先用 <see cref="TryGetCollisionWorld"/> 判断。
        /// </summary>
        public static CollisionWorldView GetCollisionWorld(this World world)
        {
            return !world.TryGetCollisionWorld(out CollisionWorldView view)
                ? throw new System.InvalidOperationException(
                    "碰撞模块未启用：请向 ticker 注册 CollisionSystemGroup（或 CollisionSetupSystem + CollisionBroadphaseSystem）并运行至少一帧")
                : view;
        }

        /// <summary>尝试取碰撞世界操作视图；未初始化时返回 false。</summary>
        public static bool TryGetCollisionWorld(this World world, out CollisionWorldView view)
        {
            view = default;
            if (!world.TryGetSingleton<CollisionWorld>(out Entity owner)) return false;
            if (!world.GetComponent<CollisionWorld>(owner).IsInitialized) return false;
            view = new CollisionWorldView(world, owner);
            return true;
        }

        /// <summary>
        /// 确保碰撞世界已初始化（系统首 tick 调用；业务侧预创建同用）。
        /// buffer 由 World 托管，重复调用为空操作。
        /// </summary>
        public static CollisionWorldView EnsureCollisionWorld(this World world)
        {
            Entity owner = world.GetOrCreateSingleton<CollisionWorld>();
            var view = new CollisionWorldView(world, owner);
            view.EnsureInitialized();
            return view;
        }

        /// <summary>当帧候选 pair 数（未启用时返回 0）。</summary>
        public static int GetCandidatePairCount(this World world) =>
            world.TryGetSingleton<CollisionWorld>(out Entity owner)
                ? world.GetComponent<CollisionWorld>(owner).CandidatePairCount
                : 0;

        /// <summary>当帧接触流形数（未启用时返回 0）。</summary>
        public static int GetContactCount(this World world) =>
            world.TryGetSingleton<CollisionWorld>(out Entity owner)
                ? world.GetComponent<CollisionWorld>(owner).ContactCount
                : 0;

        /// <summary>
        /// 读取当帧接触流形（只读视图）。
        ///
        /// 调用方不得跨结构变更持有该视图：<c>NativeArray</c> 指向 World 托管 buffer，
        /// 任何 buffer 扩容都会搬移内存。请在当帧内消费完。
        /// </summary>
        public static bool TryGetContacts(this World world, out NativeArray<ContactManifold> contacts)
        {
            contacts = default;
            if (!world.TryGetCollisionWorld(out CollisionWorldView view)) return false;
            int count = view.ContactCount;
            if (count <= 0) return false;
            NativeArray<ContactManifold> array = view.ContactArray;
            if (!array.IsCreated || array.Length < count) return false;
            contacts = array.GetSubArray(0, count);
            return true;
        }

        /// <summary>读取本帧 pair 接触事件；视图仅在当前帧内有效。</summary>
        public static bool TryGetContactEvents(this World world, out NativeArray<ContactEvent> events)
        {
            events = default;
            if (!world.TryGetCollisionWorld(out CollisionWorldView view)) return false;
            int count = view.ContactEventCount;
            if (count <= 0) return false;
            NativeArray<ContactEvent> array = view.ContactEventArray;
            if (!array.IsCreated || array.Length < count) return false;
            events = array.GetSubArray(0, count);
            return true;
        }

        /// <summary>追加当前帧 LBVH 的 AABB broad hits。</summary>
        public static bool OverlapAabb(
            this World world,
            in Aabb queryBounds,
            uint belongsToMask,
            ref NativeList<Entity> results)
        {
            return world.TryGetCollisionWorld(out CollisionWorldView view)
                && view.OverlapAabb(queryBounds, belongsToMask, ref results);
        }

        /// <summary>查询当前帧最近 AABB 射线命中。</summary>
        public static bool RaycastAabb(
            this World world,
            float3 origin,
            float3 direction,
            float maxDistance,
            uint belongsToMask,
            out CollisionRaycastHit hit)
        {
            hit = default;
            return world.TryGetCollisionWorld(out CollisionWorldView view)
                && view.RaycastAabb(origin, direction, maxDistance, belongsToMask, out hit);
        }

        /// <summary>向凸形状顶点池追加顶点，返回起始下标（供 <c>ShapeParams.ForConvex</c> 使用）。</summary>
        public static int AppendVertices(this World world, float3[] vertices)
        {
            if (vertices == null || vertices.Length == 0) return -1;

            CollisionWorldView view = world.EnsureCollisionWorld();
            int start = view.VertexCount;
            view.AppendVertices(vertices);
            return start;
        }
    }
}
