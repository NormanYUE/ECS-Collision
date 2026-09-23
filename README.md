# Ember Collision 使用手册

[English](README_EN.md)

Ember ECS 框架的碰撞包：宽相走 Burst 编译的 BVH 构建与 Morton 基数排序，
窄相生成接触流形，全部数据存在 World 托管 buffer 中，随 `World.Dispose()` 一并回收。

- **2D / 3D 统一**：维度由 `CollisionConfig.Dimension` 决定，不维护两套代码路径
- **7 种形状**：Sphere / Box / Capsule / Circle / Box2D / Capsule2D / Polygon2D
- **热路径零分配**：宽相、窄相、接触事件全程 Burst Job，scratch 由 World 托管

## 拉取包

在项目的 `Packages/manifest.json` 里用 Git URL 添加：

```json
{
  "dependencies": {
    "com.ember.collision": "https://github.com/NormanYUE/ECS-Collision.git"
  }
}
```

URL 后可以接 `#<tag 或分支名>` 钉版本：接 `#main` 跟生产分支（默认），
接 `#develop` 跟测试分支。不接即为仓库默认分支。

## 拉取依赖包

本包依赖两个 Ember 包与三个 Unity 注册表包：

| 依赖 | 来源 | 是否需要显式声明 |
| --- | --- | --- |
| `com.ember.ecs`（框架） | `https://github.com/NormanYUE/Ember-Framework.git` | **是** |
| `com.ember.core` | `https://github.com/NormanYUE/ECS-Core.git` | **是** |
| `com.unity.mathematics` | Unity 注册表 | 否，自动解析 |
| `com.unity.burst` | Unity 注册表 | 否，自动解析 |
| `com.unity.collections` | Unity 注册表 | 否，自动解析 |

**UPM 不解析传递的 Git 依赖。** 包内 `package.json` 里写的是版本号
（如 `"com.ember.core": "2.1.6"`），UPM 会拿这个版本号去注册表找——而这些包不在注册表上，
于是解析失败。因此两个 Ember 依赖必须在 manifest 里显式声明：

```json
{
  "dependencies": {
    "com.ember.ecs": "https://github.com/NormanYUE/Ember-Framework.git",
    "com.ember.core": "https://github.com/NormanYUE/ECS-Core.git",
    "com.ember.collision": "https://github.com/NormanYUE/ECS-Collision.git"
  }
}
```

依赖方向为 `Ember.Collision` → `Ember.Core` → `Ember.Framework`。
UPM 按精确版本解析，三个包要自下而上对齐版本：先更框架，再更 Core，最后更本包，
否则拿不到下层的修复。

要求 Unity 2022.3 或更高版本。

## 为实体添加碰撞

### 1. 注册系统组

```csharp
manager.GetTicker(updateIdx).Register<CollisionSystemGroup>();
```

`CollisionSystemGroup` 把整条碰撞管线封装为一次注册，组内注册顺序即管线顺序
（依赖图再按读写冲突自动分层）：

| 系统 | 职责 |
| --- | --- |
| `CollisionSetupSystem` | 为带 `Collider` 的实体补齐碰撞组件（每实体一生一次） |
| `CollisionBroadphaseSystem` | Morton 编码 + 基数排序 + BVH 构建，产出候选对 |
| `CollisionNarrowphaseSystem` | 候选对求交，写 `ContactManifold` |
| `CollisionContactEventSystem` | 比对上一帧接触状态，产出进入 / 停留 / 离开事件 |

若还需要视锥剔除与空间索引，再叠加框架的空间组（顺序无关，依赖图会自动分层）：

```csharp
manager.GetTicker(updateIdx).Register<SpatialSystemGroup>();
```

### 2. 挂 `Collider` 与姿态

```csharp
var entity = world.CreateEntity();

world.AddComponent(entity, Collider.Sphere(0.5f));

var l2w = float4x4.TRS(position, rotation, new float3(1f));
world.AddComponent(entity, new LocalToWorld { Value = l2w });
```

`Collider` 提供了全部 7 种形状的工厂方法：

```csharp
Collider.Sphere(radius, center)                        // 3D
Collider.Box(halfExtents, center)                      // 3D 长方体
Collider.Capsule(radius, halfHeight, axis, center)     // 3D 胶囊，axis: 0=X 1=Y 2=Z
Collider.Circle(radius, center)                        // 2D
Collider.Box2D(halfExtents, center)                    // 2D 矩形
Collider.Capsule2D(radius, halfHeight, axis, center)   // 2D 胶囊
Collider.Polygon2D(vertexStart, vertexCount, center)   // 2D 凸多边形，顶点来自顶点池
```

其余组件（`CollisionBody` / `CollisionFilter` / `CollisionState` / `BoundingVolume`）
由 `CollisionSetupSystem` 自动补齐，无需手工登记。挂上 `Collider` 即纳入宽相。

> **`LocalToWorld` 是姿态输入，需要由使用方维护。** 框架与 `Ember.Core` 都不写它：
> 静态体写一次即可，动态体在每帧移动/积分之后自行写入（或由业务自己的变换系统维护）。
> 采集 Job 的查询要求 `Collider` 与 `LocalToWorld` 同时存在，
> 缺 `LocalToWorld` 的实体不进碰撞管线。

### 3. 可选：过滤层与全局配置

**过滤层**——不挂则自动补 `CollisionFilter.Default`（`BelongsTo = 1`，`CollidesWith` 为全部）：

```csharp
world.AddComponent(entity, CollisionFilter.SingleLayer(2));
// 或完整指定：属于第 2 层，只与第 3 层碰撞
world.AddComponent(entity, new CollisionFilter(belongsTo: 1u << 2, collidesWith: 1u << 3));
```

**全局配置**——不设单例即用 `CollisionConfig.Default`（3D、宽相范围 ±1000、跳过静态-静态）：

```csharp
Entity cfgOwner = world.GetOrCreateSingleton<CollisionConfig>();
ref var config = ref world.GetComponent<CollisionConfig>(cfgOwner);
config = new CollisionConfig
{
    Dimension = CollisionDimension.XZ,                  // XY / XZ / XYZ
    BroadphaseWorldCenter = float3.zero,
    BroadphaseWorldHalfExtent = new float3(200f),
    MaxCandidatePairs = 1 << 20,
    MaxContacts = 1 << 20,
};
```

> 单例一旦创建就会被直接采用（缺单例才回落到 `Default`），
> 所以要创建就把字段写全，或先赋 `CollisionConfig.Default` 再改字段。
> 零值单例的维度是 `XY`、范围为零，碰撞不会正常工作。

`CollisionConfig` 其余字段：`MaxTreeDepth`、`LeafCapacity`、`SolverIterations`、
`ContactSlop`、`PositionCorrectionRate`、`SleepLinearThreshold`、`SleepTimeThreshold`、
`SkipStaticPairs`、`SkipInactivePairs`。

### 4. 静态体、禁用与运行时开关

```csharp
// 静态体：加 Ember.Core 的 Static 标记。宽相按 Chunk 判定静态归属，
// SkipStaticPairs 打开时静态-静态对直接跳过
world.AddComponent(entity, new Static());

// 整帧移出碰撞：加 Disabled 标记（宽相查询排除该实体）
world.AddComponent(entity, new Disabled());

// 运行时逐帧开关（保留组件，只切活动位）
ref var body = ref world.GetComponent<CollisionBody>(entity);
body.SetActive(false);          // 本帧不参与
bool active  = body.IsActive;
bool enabled = body.IsEnabled;
bool isStatic = body.IsStatic;
```

带 `Prefab` 标记的实体不进碰撞管线。

### 5. 组件与数据结构

| 类型 | 说明 |
| --- | --- |
| `Collider`、`ShapeParams` | 形状本体与参数（半径、半长、轴向、顶点池区间） |
| `BodyPose` | 位置 / 旋转 / 缩放，宽相由 `LocalToWorld` 分解而来 |
| `CollisionBody` | 运行时状态镜像（静态位、启用位、活动位、自身句柄） |
| `ShapeType`、`CollisionDimension`、`CollisionFilter` | 形状种类、维度、层过滤 |
| `CollisionConfig` | 全局配置单例 |
| `ContactManifold`、`ContactPoint` | 接触流形与接触点 |
| `ContactEvent`、`ContactEventPhase`、`PairKey` | 接触事件与 pair 键 |
| `CollisionState` | 每实体接触历史（`HasContact` / `HadContact` / `EnteredContact` / `ExitedContact`） |
| `Aabb`、`CollisionRaycastHit` | 包围盒与射线命中结果 |

## 消费碰撞

以下入口都是 `World` 上的扩展方法（`CollisionWorldExtensions`），
除 `ShapeQuery` 外都依赖当帧宽相已发布。返回的指针/视图**仅在当前帧内有效**。

### 接触流形

```csharp
if (world.TryGetContacts(out long contactsPtr, out int contactCount))
{
    unsafe
    {
        var manifolds = (ContactManifold*)contactsPtr;
        for (int i = 0; i < contactCount; i++)
        {
            ref readonly ContactManifold m = ref manifolds[i];
            // m.A / m.B 按实体槽位升序规范化，m.Normal 为接触法线
            for (int p = 0; p < m.Count; p++)      // Count ≤ ContactManifold.Capacity（4）
            {
                ContactPoint point = m.GetPoint(p); // Position / Separation
            }
        }
    }
}

int contactCount2 = world.GetContactCount();          // 同上，仅取数量
int pairCount     = world.GetCandidatePairCount();    // 宽相候选对数
```

### 接触事件

```csharp
if (world.TryGetContactEvents(out long eventsPtr, out int eventCount))
{
    unsafe
    {
        var events = (ContactEvent*)eventsPtr;
        for (int i = 0; i < eventCount; i++)
        {
            ref readonly ContactEvent e = ref events[i];
            // e.A / e.B，e.Phase ∈ { ContactEventPhase.Enter, Stay, Exit }
        }
    }
}
```

### AABB 重叠与光线查询

```csharp
using var results = new NativeList<Entity>(Allocator.Temp);
world.OverlapAabb(queryBounds, belongsToMask, ref results);   // belongsToMask 传 0 表示不限层

if (world.RaycastAabb(origin, direction, maxDistance, belongsToMask, out CollisionRaycastHit hit))
{
    // hit.Entity / hit.Distance / hit.Position / hit.Normal
}
```

两者都在当前帧的 LBVH 上遍历，故须在本帧宽相发布之后调用。

### 几何查询与快照

`ShapeQuery` 提供 shape → point 最近点查询与射线查询两组接口，均为 Burst 兼容纯静态函数，
不依赖 `World`，可在 Job 里用于预测、瞄准与编辑器工具。

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

// Polygon2D 的顶点池：初始化时追加一次，返回起始下标
int start = world.AppendVertices(new[] { v0, v1, v2, v3 });
world.AddComponent(entity, Collider.Polygon2D(start, 4));
```

> 顶点池随 `World` 生命周期增长，`ShapeParams.VertexStart` 是对它的下标。
> 只在初始化 / 建体时追加，不要每帧追加。

body 快照（运行时烘焙输入）：仅宽相发布后有效，只读、不可跨帧缓存。

```csharp
var view = world.GetCollisionWorld();
if (view.IsQueryReady)
{
    unsafe
    {
        var poses      = (BodyPose*)view.BodyPosesPtr;        // 长度 = view.BodyCount
        var colliders  = (Collider*)view.BodyCollidersPtr;
        var filters    = (CollisionFilter*)view.BodyFiltersPtr;
        var flags      = (byte*)view.BodyFlagsPtr;            // 含 Static 位
        var vertexPool = (float3*)view.VertexPoolPtr;         // Polygon2D 顶点，长度 = view.VertexCount
    }
}
```

快照访问器在 `IsQueryReady == false` 时抛 `InvalidOperationException`。

> 快照与两个查询入口返回**裸指针**，不是 `NativeArray`。由裸内存构造的
> `NativeArray` 其安全句柄是 `default`（`ConvertExistingDataToNativeArray` 不设
> `m_Safety`），作为 Job 容器字段会被调度期校验拒绝，在主线程索引会解引用空句柄
> 节点。裸指针没有这个问题，也与框架 chunk job 的既有形态一致。
> 元素个数分别由 `view.BodyCount` / `view.VertexCount` / 查询方法的 `out count` 给出。
> 消费方程序集需要开启 `unsafe`。

### 内存与生命周期

- 碰撞 scratch 全部由 `World` 托管，**无需 `Dispose`**，随 `World.Dispose()` 自动释放。
- 视图与指针**只在当帧有效**：任何 buffer 扩容都会搬移内存，请勿跨结构变更持有引用。
- `CollisionWorldView` 另提供容量预分配与诊断计数（`EnsureCapacity`、`OverflowCount` 等），
  用于在业务侧提前扩容、或排查「容量不足导致部分碰撞被丢弃」。
