# Changelog

All notable changes to Ember Collision.

[English](CHANGELOG_EN.md)

## [1.0.3] — 改为源码包发布（不再发预编译 DLL）

### Changed

- **包内容由 `Runtime/*.dll` 改为 `Runtime/**/*.cs` 源码。**

  **为什么**：预编译 DLL 里的 `[BurstCompile]` 不会被 Unity 的 Burst ILPP 处理 ——
  那条流水线只跑 Unity 自己编译的程序集。实测 22 个 Job（Collision 19 / Navigation 1 /
  Core 2）在运行期报 `not a known Burst entry point`，退回托管路径。
  改成源码包后由 Unity 编译，这条流水线才成立。

  **附带解决**：
  - `#if ENABLE_UNITY_COLLECTIONS_CHECKS` 之类 Unity 侧符号在源码编译下**真正有定义**，
    不会再出现「包内那段其实是死代码」（`Ember.Collision` 曾因此把碰撞管线整个跑崩）
  - 不再需要在包库里维护 `.meta`（那是 0.3.1 / 0.3.2 两次事故的根源）
  - 交付的是可读、可调试、可步进的源码

  **消费方无需改动**：包库 URL 不变，公开 API 不变。

- `Runtime/Ember.collision.Runtime.asmdef` 的 `precompiledReferences` 由指自己的 DLL
  改为 **`Ember.dll`**（框架仍以预编译形式提供）。`references` 显式列出 Unity 侧依赖
  （`Unity.Collections` / `Unity.Mathematics` / `Unity.Burst`）与上游 Ember 程序集。
- 补上 `com.unity.collections` 依赖声明 —— 之前是 DLL 包，编译期引用是 dotnet 侧的
  `libs/Unity.Collections.dll`，包本身不声明；改为源码包后必须声明。
- 依赖提升：com.ember.ecs 1.13.0、com.ember.core 2.1.3

### Notes

- 同步工具：`Ember.Framework/tools/deploy-package-sources.py`（源码库 → 包库，
  确定性生成 `.meta`）。发布流程见根目录 `CLAUDE.md`。
- 未验证：Unity 运行期。asmdef 的跨包预编译引用（`Ember.dll`）需要在 Unity 里确认能解析。

## [1.0.2] — 修复：DiagnosticFlags 从未设过长度

### Fixed

- **`CollisionBroadphaseSystem` 不再抛 `DiagnosticFlagPtr: buffer too small — requested 8 x Int32,
  buffer holds 0`。**

  根因不是「增长表达式不匹配」，而是**那一行增长调用根本不存在**：
  `World.CreateBuffer<T>(initialCapacity)` 只设**容量**，逻辑长度是 **0**
  （`BufferStore.CreateBuffer` 写的是 `Length = 0, Capacity = capacity`）。
  `EnsureInitialized` 创建的 35 个 buffer 里有 34 个后续被 `Grow` 设过长度，唯独
  `DiagnosticFlags` 一次都没有 —— 于是它永远是空 span，而宽相每帧都要读它。

  `EnsureCapacity` 里补上 `Grow<int>(ref state.DiagnosticFlags, CollisionWorld.DiagnosticSlotCount);`。

### Changed

- **35 个 buffer 的创建点全部改用 `World.CreateSizedBuffer<T>(n)`**（Ember 1.13.0 新增），
  建出来长度即等于请求值。这样「创建了但忘了设长度」这一类不再可能发生 —— 之前 34 个靠
  `Grow` 兜住、1 个漏掉，靠的是人工一致性。
- 依赖 `com.ember.ecs` 提升至 1.13.0、`com.ember.core` 提升至 2.1.2。

### Notes

- 未验证：Unity 运行期。CLI 侧编译与 227 项算法测试通过。

## [1.0.1] — 修复：容量不足时不再静默返回空指针

### Fixed

- **`NativePointer<T>` 在拿不到内存时改为抛异常，不再返回 `0L`。**

  1.0.0 引入裸指针时，让访问器在缓冲不足时静默返回 `0` —— 这是一个坏设计：
  Job 拿到空指针后按 `(T*)0` 解引用，在托管路径下退化成**没有栈的**
  `NullReferenceException`，而原先 `NativeArray` 索引器会替我们报错的那一层已经没了。

  现在改为抛 `InvalidOperationException`，用 `[CallerMemberName]` 带上访问器名与
  请求 / 实际长度，例如：

  ```
  CollisionWorldView.ContactOffsetPtr: buffer too small — requested 2049 x Int32,
  buffer holds 2048. The growth expression in EnsureCapacity / EnsurePairDependentCapacity
  does not match the length this accessor asks for.
  ```

  长度为 0 仍是合法请求（当帧没有 body / 没有 pair），照旧返回 0。

- **`ContactOffsets` / `ContactCounts` 的长度表达式与访问器对齐。**

  这是一处**可达**的空指针：`Grow` 按 2 的幂取整，两个缓冲区原先按「本次请求容量」扩，
  而访问器按 `PairCapacity`（= `CandidatePairs` 的**实际**长度）要长度。当
  `m_PredictedPairs` 不是 2 的幂时二者不等 —— 例如 pairCount = 600 时
  `m_PredictedPairs` = 1200，`ContactOffsets` 被扩到 `nextPow2(1201)` = 2048，
  而访问器请求 `PairCapacity + 1` = `nextPow2(1200) + 1` = 2049 > 2048。

  改为按 `CandidatePairs` 的实际长度扩，二者由构造保证对齐。

### Notes

- 消费方 `Ember.Navigation` 0.2.4 跟随。

## [1.0.0] — 修复：Job 层改用裸指针；快照与查询入口签名变更

### Fixed

- **碰撞管线在真实 Unity 世界里不再每帧刷错。** 原先三处报错
  （`GatherJob.BodyEntities has not been assigned or constructed`、
  `ContactFlagClearJob.BodyContactFlags ...`、接触事件系统的 NRE）同源，根因是
  **`NativeArray` 的安全句柄从未被设置**。

  `NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray` 只设
  `m_Buffer` / `m_Length` / `m_AllocatorLabel` / `m_MinIndex` / `m_MaxIndex`，
  **不设 `m_Safety`**，句柄是 `default`。后果两条：
  作为 Job 容器字段被调度期容器校验直接拒绝；在主线程索引则解引用空的
  `versionNode`。

  而补句柄的代码是**死代码**：`NativeBufferUtil` 里的 `SetAtomicSafetyHandle`
  包在 `#if ENABLE_UNITY_COLLECTIONS_CHECKS` 中，该符号在本仓库的任何 csproj /
  props 里都没有定义，`dotnet build` 也不会定义它。反编译已发布的
  `Ember.Collision.dll` 可直接证实 —— 只含 `ConvertExistingDataToNativeArray`，
  不含 `SetAtomicSafetyHandle` 也不含 `GetTempMemoryHandle`。

  补句柄的两条路都不通：`GetTempMemoryHandle()` 返回的是**当前临时内存作用域**的
  句柄（其配套 API `IsTempMemoryHandle` 的文档写明语义），不是「本帧有效」的保证；
  `AtomicSafetyHandle.Create()` 必须配对 `Release()`，而本模块的资源模型是
  「buffer 随 `World.Dispose` 自动释放、无需 Dispose」，引入句柄就得再加一套生命周期。

  故改用 `Ember.Framework` chunk job 的既有做法：**把裸指针放进
  `[NativeDisableUnsafePtrRestriction] public long` 字段，Job 内部自行转型。**
  本视图类里原本就有一个这种形态的 `VertexPointer`。

### Changed

- **`CollisionWorldView` 的 body 快照改为裸指针**，属性名加 `Ptr` 后缀：
  `BodyPoses` → `BodyPosesPtr`、`BodyColliders` → `BodyCollidersPtr`、
  `BodyFilters` → `BodyFiltersPtr`、`BodyFlags` → `BodyFlagsPtr`、
  `VertexPool` → `VertexPoolPtr`。元素个数由 `BodyCount` / `VertexCount` 给出。
- **`World.TryGetContacts` / `World.TryGetContactEvents` 改为
  `out long ptr, out int count`。**
- 内部 36 个 `*Array` 视图属性一并改为 `*Ptr`；新增
  `PairCapacity` / `ContactCapacity` / `ContactPairCapacity` /
  `ContactEventCapacity` / `ChunkCount` —— 视图改成裸指针后 Job 侧拿不到
  `NativeArray.Length`，容量必须显式传入。
- `NativeBufferUtil.AsNativeArray` 标 `[Obsolete]`（保留以免破坏已编译的消费方），
  新增 `NativeBufferUtil.AsPointer`。

### Breaking

公开 API 有两处签名变更。之所以仍发布为修复：**原签名返回的 `NativeArray` 从来没有
有效句柄，任何索引都会失败**，它不是一个能用的接口。

### Notes

- 消费方 `Ember.Navigation` 0.2.3 已同步（`NavRuntimeBake`、
  `NavDynamicObstacleSystem` 改吃裸指针，快照拷贝改用 `UnsafeUtility.MemCpy`）。
- 未验证：Unity 运行期。CLI 侧已验证编译、全部 227 项算法测试，
  并用 Unity 2022 工程实际解析到的程序集（Collections 2.4.3 / Burst 1.8.21 /
  Mathematics 1.3.2）整包重编通过。

## [0.3.2] — 修复：系统构造早于 World 创建时抛未注册组件

### Fixed
- **按官方装配顺序注册 `CollisionSystemGroup` 不再抛 `unregistered component type`。**

  `SystemTicker.Register` 会**立即构造**系统，而它在官方示例里早于 `ECSManager.Start()`，
  也就早于 `World` 创建；但组件类型注册原本只发生在 `World` 构造函数里。系统的字段初始化器
  一旦构造 `EntityQuery`（`ComponentMask.With<T>()` 当场读 `ComponentTypeRegistry`），
  全新 AppDomain 的第一次运行就必然抛异常。

  受影响：`CollisionSetupSystem`、`CollisionBroadphaseSystem`、`CollisionNarrowphaseSystem`。
  三者的 query 字段去掉初始化器与 `readonly`，改在 `OnCreate()` 内构造 —— `OnCreate` 由
  `SystemTicker.Init` 调用，晚于 `World` 构造；`BuildAccess` 紧随其后，`DeclareAccess`
  不依赖这些字段。

### Changed
- 依赖 `com.ember.ecs` 由 1.11.0 提升至 1.12.0、`com.ember.core` 由 2.1.0 提升至 2.1.1
  （两者都修了同一缺陷；UPM 按精确版本解析，不跟版本拿不到修复）。

### Notes
- 框架侧（1.12.0）保证了经由 `SystemTicker` 的装配路径，包侧则不再依赖「构造期读全局状态」
  这一副作用。两处都改是刻意的。

## [0.3.1] — 补齐 .meta（Unity 忽略不可变包中无 meta 的资源）

### Fixed

- 包内全部资源补齐 `.meta`（6 个文档/配置 + `Runtime` 目录 + DLL + asmdef）。

  Unity 对不可变包目录（`Library/PackageCache`）中缺少 `.meta` 的资源**直接忽略**，
  于是 `Ember.Collision.dll` 从未被加载，任何引用它的程序集都会报
  `Unable to resolve reference 'Ember.Collision'` —— 表现为下游包（如 Ember.Navigation）
  整个程序集不加载。

  这是打包遗漏，**DLL 内容未变**，只是补上 Unity 要求的元数据。

## [0.3.0] — 精确射线查询（导航依赖交付）

### Added

- **`ShapeQuery.Raycast`**（N3）：射线 vs 单个形状的最近相交，覆盖全部 7 种 `ShapeType`。
  `direction` 无需归一化（内部归一化），返回沿射线方向的距离、世界命中点与指向来向的
  单位外法线。Burst 兼容纯静态函数、无托管分配。
  - 边界语义明确：起点在形状内或表面上（有符号距离 ≤ 0）立即命中（`distance = 0`）；
    零方向、`maxDistance` 为负视为不命中；`maxDistance` 为 0 时仅内起点命中；
    相切（判别式为 0）算命中；不命中时 `distance = 0`、`point = origin`、`normal` 归零。
  - 2D 形状按「沿无效轴无限延伸的棱柱」在 `CollisionDimension` 平面内求解，
    `point` 取射线上对应参数处的真实坐标（含无效轴分量）；射线垂直于平面且投影落在
    形状外时不命中。
  - 胶囊走柱面二次方程 + 两端半球帽，端帽球与柱段重合的那半不算表面；
    盒走局部空间 slab 测试（复用 `CollisionQueryMath.TryRayAabb`），
    凸多边形走逐边求交取最近命中。
  - 多边形需传顶点池重载；非多边形调用便捷重载时不传顶点池。

### Changed

- 依赖 `com.ember.ecs` 由 1.10.1 提升至 1.11.0、`com.ember.core` 由 2.0.0 提升至 2.1.0。
- 测试扩充至 229 项：CLI 227 项通过；2 项 Unity 宿主桩测试在 Unity Test Runner 中执行。

## [0.2.0] — 几何查询与 body 快照（导航依赖交付）

### Added

- **`ShapeQuery.ClosestPoint`**（N1）：shape → point 最近点 / 有符号距离查询，
  覆盖全部 7 种 `ShapeType`。返回值为正（外）/ 负（内，-穿透深度）/ 零（表面），
  `normal` 为最近点指向查询点的单位向量；2D 形状按 `CollisionDimension` 语义
  （XY / XZ）抬升到 3D。Burst 兼容纯静态函数、无托管分配。
  多边形形状需传顶点池重载；退化输入（零半径球、零半范围盒、退化胶囊、
  顶点数 < 3 的多边形）行为明确。
- **`CollisionWorldView` 公开 body 快照**（N2）：`BodyPoses` / `BodyColliders` /
  `BodyFilters` / `BodyFlags` / `VertexPool` 只读 `NativeArray` 视图，
  供运行时烘焙等消费方读取。仅当帧宽相完整发布后（`IsQueryReady == true`）有效，
  只读、不可缓存跨帧；提前读取抛 `InvalidOperationException`。零分配。

### Perf

- `CollisionWorldView.Grow` 扩容从「逐元素 Add 循环」改为单次 `ResizeBuffer`
  （需要 Ember 框架 ≥ 1.11.0）。

## [0.1.0] — 首次发布

### Added

- 宽相：Morton 编码、基数排序与 BVH 构建，全部为 Burst 编译的 Job。
- 窄相：候选对求交并生成 `ContactManifold` / `ContactPoint`。
- 事件：`CollisionContactEventSystem` 按上一帧接触状态产出进入、停留、离开事件。
- 数据类型：`Collider`、`CollisionBody`、`BodyPose`、`ShapeParams`、`ShapeType`、
  `CollisionDimension`、`CollisionFilter`、`CollisionConfig`、`CollisionState`、
  `Aabb`、`PairKey`、`CollisionRaycastHit`。
- 系统组：`CollisionSystemGroup` 聚合 Setup / Broadphase / Narrowphase / ContactEvent 四个系统。
- 查询入口：`World` 扩展方法 `EnsureCollisionWorld`、`TryGetContacts`、`TryGetContactEvents`、
  `OverlapAabb`、`RaycastAabb`，以及 `CollisionWorldView` 的树操作。
- 全部状态存放于 World 托管 buffer，随 `World.Dispose()` 自动释放。
