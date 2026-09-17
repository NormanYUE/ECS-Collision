# Ember Collision 使用手册

[English](README_EN.md)

Ember ECS 框架的碰撞包。宽相走 Burst 编译的 BVH 构建与 Morton 基数排序，
窄相生成接触流形，全部数据存在 World 托管 buffer 中，随 `World.Dispose()` 一并回收。

## 安装

本包依赖 `com.ember.ecs` 与 `com.ember.core`。通过 Unity Package Manager 安装：

1. 打开项目的 `Packages/manifest.json`
2. 添加依赖（版本号以实际发布为准）：

```json
{
  "dependencies": {
    "com.ember.ecs": "1.10.1",
    "com.ember.core": "2.0.0",
    "com.ember.collision": "0.1.0"
  }
}
```

3. 若通过 Git URL 安装：UPM 不解析传递 Git 依赖，请确保 `com.ember.ecs` 与 `com.ember.core` 已在 manifest 中显式声明。

要求 Unity 2022.3 或更高版本。

## 系统

`CollisionSystemGroup` 按顺序驱动以下系统，注册到 World 后自动参与依赖图调度：

| 系统 | 职责 |
| --- | --- |
| `CollisionSetupSystem` | 同步碰撞体数据、初始化解算所需的中间缓冲 |
| `CollisionBroadphaseSystem` | Morton 编码 + 基数排序 + BVH 构建，产出候选对 |
| `CollisionNarrowphaseSystem` | 候选对求交，生成 `ContactManifold` |
| `CollisionContactEventSystem` | 比对上一帧接触状态，产出进入/停留/离开事件 |

## 数据

组件与配置本体放在 `Ember.Collision` 命名空间：

- `Collider`、`CollisionBody`、`BodyPose`、`ShapeParams` —— 碰撞体与形状
- `ShapeType`、`CollisionDimension`、`CollisionFilter` —— 形状种类、维度与过滤层
- `CollisionConfig` —— 全局开关（宽相策略、接触容量等）
- `ContactManifold`、`ContactPoint`、`ContactEvent`、`PairKey` —— 接触与事件数据
- `CollisionState`、`Aabb`、`CollisionRaycastHit` —— 状态、包围盒与查询结果

## 查询入口

`World` 上的扩展方法（`CollisionWorldExtensions`）：

```csharp
world.EnsureCollisionWorld();                                  // 建树并注册单例
if (world.TryGetContacts(out long contacts, out int contactCount)) { /* 本帧接触流形 */ }
if (world.TryGetContactEvents(out long events, out int eventCount)) { /* 本帧进入/离开事件 */ }
world.OverlapAabb(aabb, ref results);                          // AABB 重叠查询
world.RaycastAabb(origin, direction, maxDistance, out var hit); // 射线查询
world.GetCandidatePairCount();                                 // 宽相候选对数
world.GetContactCount();                                       // 接触数
```

`CollisionWorldView` 提供树的插入、更新、移除与查询操作；
`Ember.Core` 提供的 `WorldBounds` / `LocalToWorld` 可直接参与宽相输入。

## 几何查询与快照

`ShapeQuery` 提供 shape → point 最近点查询与射线查询两组接口，均为 Burst 兼容纯静态函数。

```csharp
// shape → point 最近点 / 有符号距离（外正内负），覆盖全部 7 种形状
float d = ShapeQuery.ClosestPoint(collider, pose, dimension, point,
    out float3 closest, out float3 normal);
// Polygon2D 传顶点池重载
float d2 = ShapeQuery.ClosestPoint(collider, pose, dimension, point,
    vertexPoolPtr, vertexPoolLength, out closest, out normal);

// 射线 vs 单个形状：返回沿射线的距离、世界命中点、指向来向的单位外法线
// direction 无需归一化；起点在形状内 / 表面上时 distance = 0
bool hit = ShapeQuery.Raycast(collider, pose, dimension, origin, direction, maxDistance,
    out float distance, out float3 point, out float3 normal);
// Polygon2D 传顶点池重载
bool hit2 = ShapeQuery.Raycast(collider, pose, dimension, origin, direction, maxDistance,
    vertexPoolPtr, vertexPoolLength, out distance, out point, out normal);

// body 快照（运行时烘焙输入）：仅宽相发布后有效，只读、不可缓存跨帧
var view = world.GetCollisionWorld();
if (view.IsQueryReady)
{
    unsafe
    {
        var poses      = (BodyPose*)view.BodyPosesPtr;        // 长度 = view.BodyCount
        var colliders  = (Collider*)view.BodyCollidersPtr;
        var filters    = (CollisionFilter*)view.BodyFiltersPtr;
        var flags      = (byte*)view.BodyFlagsPtr;            // 含 Static 位
        var vertexPool = (float3*)view.VertexPoolPtr;         // Polygon2D 顶点
    }
}
```

快照访问器在 `IsQueryReady == false` 时抛 `InvalidOperationException`；
`ShapeQuery` 为 Burst 兼容纯静态函数，无托管分配。

> 快照与两个查询入口返回**裸指针**，不是 `NativeArray`。由裸内存构造的
> `NativeArray` 其安全句柄是 `default`（`ConvertExistingDataToNativeArray` 不设
> `m_Safety`），作为 Job 容器字段会被调度期校验拒绝，在主线程索引会解引用空句柄
> 节点。裸指针没有这个问题，也与框架 chunk job 的既有形态一致。
> 元素个数分别由 `view.BodyCount` / `view.VertexCount` / 查询方法的 `out count` 给出。

## 说明

- 版本号使用纯 `a.b.c`；破坏性变更递增 `a`，新功能递增 `b`，修复与优化递增 `c`。
- 本仓库只存放编译产物，源码在私有仓库维护。
