# Ember.Collision 设计文档

Ember 框架 + Ember.Core 之上的 2D/3D 碰撞模块。目标：并行 Job、Burst、0GC、海量实体。
**2D/3D 统一**：维度由 `CollisionConfig.Dimension` 决定，不维护两套代码路径。

---

## 1. 框架硬约束（已逐条核实，决定全部设计取舍）

| # | 事实 | 出处 | 影响 |
|---|---|---|---|
| 1 | `JobSystem<TJob>` 只能表达 chunk 并行（`IEmberChunkJob`） | `Ember/src/Systems/JobSystemBase.cs` | 窄相/求解必须自调度 Job |
| 2 | `SystemBase.OnTick` 无 JobHandle 返回通道 | `Ember/src/Systems/SystemBase.cs` | 自调度必须在 OnTick 内 `Complete()` |
| 3 | `WorldSafety` 为 internal | `Ember/src/Core/WorldSafety.cs` | **禁止跨 tick 挂起 Job**，否则框架结构变更保护失效 |
| 4 | `Chunk.GetEntityPtr/GetBufferPtr` 为 internal | `Ember/src/Storage/Chunk.cs` | Entity 句柄必须存进组件（`CollisionBody.Self`），Job 才能从列里读到 |
| 5 | `ChunkColumn<T>.UnsafePtr` 为 public，`GetColumn<T>()` 为 O(1) | `Ember/src/Storage/Chunk.cs:141` | 串行侧每帧采集列指针表；实测成本 O(Chunk 数) |
| 6 | buffer 扩容用「另分配 + 拷贝」，**搬移全部 range 地址** | `Ember/src/Buffer/BufferStore.cs` | 容量必须在调度前决定；Job 执行期严禁扩容 |
| 7 | `ChunkJobMeta` 仅 4 个内联 slot | `Ember/src/Systems/ChunkJobScheduler.cs` | `Collider` 用单一 union 组件而非每形状一个组件 |
| 8 | `PairQuery` 回调为 `Action<...>` | `Ember/src/Query/PairQuery.cs` | 托管委托，热路径禁用 |
| 9 | Tag 组件只占 Archetype 掩码、**没有列** | `Ember/src/Storage/Archetype.cs` | `Static` 用 **Chunk 级**标志镜像，避免 O(N) 逐实体同步 |

---

## 2. 数据流

```
Collider + LocalToWorld
   │
   ├─ CollisionSetupSystem（串行·ECB）      补 CollisionBody/Filter/State/BoundingVolume
   │
   └─ CollisionBroadphaseSystem（串行外壳 + 内部并行流水线）
        GatherJob           按 Chunk 并行   列 → 稠密数组；写回 BoundingVolume
        BoundsReduceJob     按块并行       全局 AABB 归约
        BoundsReduceFinal   串行
        MortonJob           按 body 并行    空间键（2D 20 位 / 3D 30 位）
        RadixSort ×趟×4 步  并行           稳定 LSD 基数排序
        BvhLeaf/BvhMerge    并行           补足到 2 的幂的完美二叉树
        BvhFinalize         串行           顶层收尾
        PairCountJob        按叶并行       每叶只向更大排序位置推进 → 天然去重
        Scan 三步           并行+串行      pair 偏移前缀和
        ── 同步① ──                        读出精确 pair 数
        PairCollectJob      按叶并行       写入 CandidatePair（A<B 归一化）

   └─ CollisionNarrowphaseSystem（串行外壳 + 内部并行流水线）
        ContactFlagClearJob  按 body             清除本帧接触标志
        ContactCountJob      按 candidate pair   精确形状判定 → 0/1 流形数
        Scan 三步            并行+串行           contact 偏移前缀和
        ── 同步② ──                               读出精确流形数 / 按 MaxContacts 扩容
        ContactCollectJob    按 candidate pair   写入确定性单流形（A→B 法线）
        ContactFlagMarkJob   串行归约             命中 pair → dense body 接触标志
        ContactStateWrite    按 Chunk            写回当前 / 上一帧接触位

   └─ CollisionContactEventSystem
        ContactPairGather    按 candidate pair   收集未截断的真实接触 membership
        ContactPairSort      串行 Burst          按 canonical pair key 稳定排序
        ContactEventMerge    串行                归并前后帧 → Enter / Stay / Exit

   └─ OverlapAabb / RaycastAabb              当前帧 LBVH broad query
   └─ CollisionSolverSystem                  【待实现 P4】
```

**写回 `BoundingVolume`** 是最关键的复用接线：框架既有的 `WorldBoundsSystem`
随即算出 `WorldBounds`，于是碰撞体**自动**获得视锥剔除与空间索引能力（`SpatialSystemGroup` 零改动）。

---

## 3. 0GC 与确定性

**0GC**
- 全部 scratch 走 World 托管 `BufferHandle`（单例组件持句柄 + View 操作），随 `World.Dispose` 自动释放，无 Dispose、无泄漏。
- 容量在串行侧按当帧实体数一次性扩容（2 的幂取整），增长次数 O(log n)。
- 并行输出用「计数 → 前缀和 → 散布」两段式，替代 `NativeList` 竞争追加：无锁、无原子操作、零分配。
- 输出顺序只取决于 Morton 排序（稳定 LSD 基数排序），因此**同输入必得同输出**。

**确定性**
- 基数排序是稳定排序 → pair 顺序确定。
- BVH 是补足到 2 的幂的完美二叉树 → 结构确定。
- pair 去重靠「只向排序位置更大者推进」，不依赖哈希集合 → 结果与遍历顺序无关。

---

## 4. 算法核心刻意与 Job 解耦

宽相的全部算法写在**裸指针静态函数**里（`BvhBuilder` / `RadixSort32` / `BlockScan`），
Job 只做薄包装。原因：纯 .NET CLI 无法分配 `NativeArray`，
若算法只存在于 Job 内，则这些最容易出隐蔽 bug 的逻辑在 Unity 之外**完全无法验证**。

由此换来 109 项可执行测试，其中对拍类断言包括：
- BVH 自查询的 pair 集合与暴力 O(n²) **完全相等**（既不漏也不重）
- 补位空叶不参与 pair，且不抬高 `MaxLeaf` 上界
- `order[]` 为乱序置换时仍完整
- 基数排序结果与 CLR 稳定排序逐项一致；等键元素相对顺序不变
- 前缀和区间**恰好无缝铺满** `[0, total)`（不重叠、无空洞）
- 旋转后 8 个角点全落在世界 AABB 内（穷举证明「不漏包」）
- Morton 位散布在 10 位全域上双射（位冲突会导致偶发漏检）

---

## 5. 已修复的真实缺陷

> 大部分由纯逻辑单测暴露；**最后一条是靠宿主侧实测发现的**——它输出正确、只是慢，
> 单测与结果断言都抓不到，这也印证了「Job 接线不在 CLI 覆盖范围内」这个已知盲区。

1. **Shepperd 四元数分量顺序错乱**：`FromOrthonormalBasis` 把 `(w,x,y,z)` 当 `(x,y,z,w)` 构造，
   解出的旋转被完全打乱。由轴角扫描测试（覆盖 4 个分支）捕获。
2. **`uint >> 32` 反噬**：C# 对 `uint` 的位移量按 5 位掩码，`>> 32` 等价 `>> 0`，
   多余趟会把已排好的低位重新打乱。已定义为「稳定的空操作」。
3. **复用遍历栈当输出缓冲**：命中数可能远超栈深，会把 DFS 栈挤爆。
   改为直接在核心层写 `CandidatePair`，并让**测试跑生产路径**。
4. **`EnsureCapacity` 提前返回**：初始化后直接 return，永不扩容。
5. **`RootIndex` 依赖 Job 输出**：主线程读不到。改用解析解 `2 * leafCapacity - 2`，
   顺带省掉一次流水线同步。

6. **`BvhLeafJob` 每次执行重建整张叶数组 → 宽相 O(leafCapacity²)**（1.0.15 修复）。
   `Execute(int leafIndex)` 拿到下标却没用，直接调了写整张数组的 `BuildLeaves`；
   而该 Job 以 `Schedule(leafCapacity, 64)` 派发，于是变成「leafCapacity 次执行 × 每次
   leafCapacity 个叶」。因为每次执行都写出同样的正确结果（幂等），
   **编译门、单测、结果断言全部通过**，只在实测里表现为「每 body 成本随 2 的幂阶梯跳变」。

   实测指纹（Sample12，关闭 deep profiling）：

   | bodies | leafCapacity | 每 body 成本 |
   | --- | --- | --- |
   | 818 | 1024 | 2.97 µs |
   | 1049 | 2048 | **4.30 µs** ← 跨 2 的幂 |
   | 1133 | 2048 | 4.57 µs |

   修复后恒定 1.30–1.36 µs；宽相 3.936 → 0.305 ms（12.9×）。
   同时全包审计了其余 15 个 `IJobParallelFor`，只有这一处是「并行分发 + 写整张数组」。

---

## 6. 与 Ember / Ember.Core 的复用关系

**复用**：`LocalToWorld` / `BoundingVolume` / `WorldBounds` / `WorldBoundsSystem` /
`SpatialSystemGroup` / `SpatialTree`（gameplay 查询）/ `Static` / `Disabled` / `Prefab` /
`Prefab 模板排除` / `ParentComponent` / `SystemGroup` 注册范式 /
`VisibilityState` 的双位边沿范式（`CollisionState` 照搬）/ `SpatialSetupSystem` 补组件范式 /
`ctx.ECB` / `[assembly: EmberJobCompilation(Burst)]`。

**不复用（附理由）**：
- `SpatialTree` 当宽相：串行（`BufferSpan` 链表 + `ref NativeList<Entity>` 结果容器）、
  **只回答「谁与这个 AABB 相交」而不产生 pair 集合**、删除延迟一帧。
  → 保留用途：gameplay 射线/重叠查询 + **测试 oracle**（逐实体 `QueryAABB` 暴力对拍）。
- `PairQuery` / `AllPairsPartitioner`：`Action` 委托，会分配。

---

## 7. 后续路线（按已确认的「全做」）

| 阶段 | 内容 | 状态 |
|---|---|---|
| P0 | 骨架、组件、配置、系统组、csproj 依赖 | ✅ 完成 |
| P1 | 宽相：采集 / 归约 / Morton / 基数排序 / LBVH / 候选 pair | ✅ 完成（109 项测试通过） |
| P2 | 窄相：3D `Sphere`/`Box`/`Capsule`；2D `Circle`/`Box2D`/`Capsule2D`/`Polygon2D` | ✅ 完成（单流形 / pair，count→scan→collect） |
| P3 | 接触边沿事件、`ContactEvent`、射线/重叠查询 | ✅ 完成（pair events + 当前帧 AABB query） |
| P4 | 求解：顺序冲量 + 摩擦 + 位置修正 + 接触图着色并行 | ⏸️ 物理响应延期 |
| P4-prep | 非物理接触图着色：canonical pair 的确定性约束分组 | ✅ 完成 |
| P5 | 优化：消除流水线同步、块并行 BVH 构建、SIMD 4-wide、静态段增量 | ⬜ 待做 |

### P2 实施要点
- 形状对分派：`switch ((int)A * ShapeCount + (int)B)` → 内联静态方法，Burst 友好。
- SAT 轴数由维度决定：3D Box-Box 15 轴，2D Box2D-Box2D 4 轴（2 面 + 2 边）。
- 流形固定 4 点容量，blittable。
- 同样两段式 count→prefix→scatter（复用 `ScanBlockJob` 三件套）。
- 当前 P2 每个命中 pair 写一个确定性的 witness 点；多点面裁剪留给 P4 的求解稳定性工作。
- 2D `Box2D` / `Capsule2D` 的参数为局部 U/V：U 为 X，V 在 XY 为 Y、在 XZ 为 Z；
  Polygon2D 顶点是凸形状的局部平面坐标。只保证刚体变换和非负等比缩放的精确语义。
- P2 对层掩码、启用 / 活动位和静态-静态跳过做双向过滤；`MaxContacts` 只截断已发布流形，
  不影响 `CollisionState` 的真实接触状态。
- 宽相计数或收集发生溢出时会记录诊断并 fail-closed 发布 0 个 pair，绝不让部分写入的尾部
  进入 P2 产生陈旧接触。
- P2 更新 `CollisionState` 当前 / 历史位；P3 再消费完整 pair membership 生成事件与当前帧 broad query。
- **每个形状对都必须有自己的解析解测试**；未经验证的几何代码会静默产生错误法线/穿透深度，
  这类 bug 在运行时只表现为「手感不对」，无法通过编译门或集成测试发现。

### P3 说明
- `ContactEvent` 是 pair-level：同一实体从 A-B 切换到 A-C 会产生 A-B Exit 与 A-C Enter，
  而不会被聚合 `CollisionState` 掩盖。pair membership 来自未受 `MaxContacts` 截断的 P2 计数流。
- `OverlapAabb` 与 `RaycastAabb` 是当前帧 LBVH 的 AABB broad query；它们不承诺精确 shape hit。
  需要泛用 overlap 查询时，可复用 Ember.Core 的 `SpatialTreeView.QueryAABB/QuerySphere`。

### P4 边界
- `ContactGraphColoring` 仅为后续求解提供 deterministic color / execution-order 基础；它不调度 Job，
  不施加冲量或摩擦，不做位置修正或睡眠，也不读写 ECS transform / velocity。
- 当前模块没有质量、惯量、材质、速度或安全的 transform writeback 所有权。物理响应延期，
  避免向派生 `LocalToWorld` 写入造成层级变换错误。
- `CollisionConfig` 中的 solver / slop / correction / sleep 字段在 solver 注册前均为预留参数。

### 当前已知的同步点
宽相计数结束后读取精确 pair 数；窄相计数结束后读取精确流形数；P4 求解前仍将有一处。
同步换来「不为最坏情况预留数百 MB」。消除方案：按上帧数量预测 + 容量溢出后重跑（P5）。

---

## 8. 验证现状与限制

- **可执行**：编译门 + 165 项纯逻辑单测（本机 .NET CLI），覆盖宽相核心、P2/P3 纯逻辑、资格过滤和 P4-prep coloring。
- **不可执行**：依赖 `NativeArray` 的端到端与性能验收——`Unity.Collections` 原生容器
  只能在 Unity 运行时分配，纯 CLI 下必须 `Assert.Ignore`（Ember 框架自身测试同此约定）。
  因此**帧预算、Burst 编译产物、实际并行度、IL2CPP 表现均未经验证**，
  必须在 Unity 宿主中做验收（1k / 10k / 100k / 1M 实体，断言每帧 0 分配）。

## 9. 构建

```bash
DOTNET=/Users/norman/.dotnet/dotnet
$DOTNET build Ember.Collision.csproj -c Release
$DOTNET test tests/Ember.Collision.Tests/Ember.Collision.Tests.csproj -c Release
```

MSBuild 可覆盖属性：`EmberFrameworkDir` / `EmberRuntimeDll` / `EmberCoreDll` / `EmberGeneratorDll`。
