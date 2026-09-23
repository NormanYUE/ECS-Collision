# Changelog

All notable changes to Ember Collision.

[English](CHANGELOG_EN.md)

## [1.0.15] — 修复 BVH 叶层构建的 O(n²)（宽相 3~13 倍加速）

### Fixed

- **`BvhLeafJob` 每次执行都重建「整张」叶数组，导致宽相构建退化成 O(leafCapacity²)。**

  ```csharp
  public unsafe void Execute(int leafIndex)   // leafIndex 完全没被用到
  {
      BvhBuilder.BuildLeaves(nodes, bounds, order, BodyCount, LeafCapacity);  // 写全部 leafCapacity 个叶
  }
  ```
  而该 Job 是用 `Schedule(leafCapacity, 64)` 派发的 —— 于是变成
  **leafCapacity 次执行 × 每次 leafCapacity 个叶**。leafCapacity = 2048 时是 **419 万次叶写入**
  （= 对 1133 个 body 做 4095 节点的工作）。

  **输出仍然是正确的**：每次执行都写出同样的正确结果，是幂等的。所以这个 bug 只表现为慢，
  编译门、单测、结果断言全部抓不到 —— 它是**靠实测**发现的（见下）。

  修法：抽出 `BvhBuilder.BuildLeafAt(nodes, bounds, order, bodyCount, leafIndex)` 做单格写入，
  `BuildLeaves` 改为循环调用它（对外语义不变），`BvhLeafJob.Execute` 只写自己那一格。

- 顺带全包审计：其余 15 个 `IJobParallelFor` 均在方法体里正确使用自己的下标，
  **只有 `BvhLeafJob` 一处**是「并行分发 + 调用写整张数组的辅助函数」这个病征。

### Performance

实测（Samples Sample12，关闭 deep profiling；同一探针、同一粒度的前后对照）：

| units | bodies | leafCapacity | 修复前 SIM | 修复后 SIM | 提速 |
| --- | --- | --- | --- | --- | --- |
| 700 | 818 | 1024 | 2.477 ms | **1.063 ms** | 2.33× |
| 950 | 1049 | 2048 | 4.508 ms | **1.427 ms** | 3.16× |
| 1000 | 1133 | 2048 | 4.857 ms | **1.489 ms** | 3.26× |

**每 body 成本从 2.97 → 4.30 → 4.57 µs（跨 2 的幂跳变）变为恒定 1.30–1.36 µs** ——
「2 的幂悬崖」消失，这正是 O(leafCapacity²) 的指纹：`leafCapacity = nextPow2(bodyCount)`
每翻一倍，叶层工作量变成 4 倍。

系统级（1000 单位）：`CollisionBroadphaseSystem` **3.936 → 0.305 ms（12.9×）**，
碰撞包合计 4.076 → **0.460 ms**，占整个 sim tick 从 81% 降到 33%。

### 测试与限制

- 新增 `BuildLeafAt_WritesExactlyOneSlot`：断言单次调用**只写一格**、且乱序写入等价于批量写入。
- **但它抓不到原 bug**：原 bug 在 Job 接线，而 CLI 测试只能覆盖纯函数（`NativeArray` / Job
  无法在纯 CLI 下分配，Ember 框架自身测试同此约定）。这类「接线上写错、结果却仍然正确」的缺陷
  **只能靠宿主侧实测**发现。本条的回归防线是上面那张实测表，不是单测。

## [1.0.14] — 消除流水线同步：宽相 / 窄相各只停一次

### Performance

- **宽相：把「计数 + 前缀和」与「写入候选 pair」合并成一条依赖链，每帧只 `.Complete()` 一次。**

  旧实现跑到计数 / 前缀和就停下、读精确 pair 数、据此扩容量，再排写入 pair ——
  即每帧固定多一次主线程排空。现在容量按**上帧 pair 数 ×2 预测**（`m_PredictedPairs`），
  整条链一次排完；预测不够时按精确总数扩容后补跑一次收集。

- **补跑的完整性判据是「精确总数 ≤ 容量」，不用哨兵。**

  前缀和每一项就是该叶要写的数量，容量足够时收集必然写满。
  `PairCollectJob` 原先在被截断时写 `-1` 哨兵，但 `CollectPairDiagnostics` 会把哨兵清零，
  而哨兵会覆盖掉补跑所需的 `expected` 计数 —— 所以哨兵与「补跑」方案互斥，已移除。

- **窄相同样改造**：容量按上帧流形数预测（`m_PredictedContacts`），
  「清除标志 → 计数 / 前缀和 → 写入流形 → 标记接触位 → 写回 CollisionState」一次排完。
  截断只影响收集（`flag mark` 用的是未截断的计数流、状态写回用的是 flag），
  因此补跑只重做收集这一步，一次必然成功。

- 稳态每帧主线程 `.Complete()` 次数：**8 → 3**。剩下三处分别是宽相链尾、窄相状态写回、
  接触事件收集→排序。后两者受框架硬约束 #2（`SystemBase.OnTick` 无 JobHandle 返回通道，
  自调度必须在 OnTick 内 Complete）限制，在框架不变的前提下无法再省。

### Fixed

- **窄相发布数量可能超过实际写入数量。** 预测容量大于实际流形数时，`outputLimit` 会大于
  真实写入的流形数，`TryGetContacts` 会按它返回一截陈旧尾部。现在发布数量固定夹到
  `min(精确流形数, MaxContacts)`。

## [1.0.13] — 宽相预筛未参与体 + 减少主线程停等

### Added

- **`CollisionConfig.SkipInactivePairs`（默认开）：宽相不再为「未参与碰撞」的体生成候选 pair。**

  判定用的是 **与窄相 `IsPairEligible` 完全相同的那组位**（`CollisionBody.EnabledBit | ActiveBit`），
  因此语义与结果完全不变，只是不再白做：这类 pair 必然被窄相丢掉。

  过滤在 `BvhBuilder.CountHierarchyOverlaps` / `CollectPairsInto` 里以**可选参数**形式实现
  （`bodyFlags` / `participationBits`，不传即不过滤），两个入口的自身侧与对手侧都会检查，
  所以「任一端未参与」的 pair 一律不产出。因为逻辑落在纯函数里，CLI 单测能直接对拍。

  实测效果（Samples Sample12，1000 单位）：**37% 的 body 是未参与体，而 60.3% 的候选 pair
  带未参与端点** —— 这部分遍历、计数与散布现在都省掉了。

### Performance

- **去掉 3 处多余的主线程排空（`.Complete()`）**：它们本可以依赖链起来一次等。

  | 位置 | 之前 | 现在 |
  | --- | --- | --- |
  | `CollisionContactEventSystem` | 收集 pair 后 Complete 一次，排序后再 Complete 一次 | 收集 → 排序链成一条依赖，只等一次 |
  | `CollisionNarrowphaseSystem` | flag mark / contact collect 各自 Complete，再单独跑状态写回并 Complete | 三者链成一条依赖，只等一次 |

  单系统 `.Complete()` 调用点从 9 处降到 6 处。剩下的两个是真正的容量决策同步点
  （读精确 pair 数 / 精确流形数），属于 P5 的「按上帧数量预测 + 溢出重跑」才能消掉的那类。

## [1.0.12] — 查询热路径优化：单例只解析一次 + 节点零拷贝

### Performance

- **`OverlapAabb` / `RaycastAabb` 每次查询都要重新解析近十个属性。**

  `CollisionWorldView.State` 是 `World.GetComponent<CollisionWorld>(owner)` 的
  **按值拷贝**；而每个 `xxxPtr` 访问器除了再拷一次整块状态，还要 `GetBuffer` 一次。
  一次 LBVH 查询会用到 `IsQueryReady / BodyCount / BvhNodePtr / SortedOrderPtr /
  BodyEntityPtr / BodyFilterPtr / BodyFlagPtr / TraversalStackPtr / LeafCapacity`
  近十个属性，于是解析开销盖过了遍历本身。

  实测（deep profile，126 次查询 / 帧）：每个 `get_*Ptr()` 单次 2.6–3.6 µs。
  现在两个入口各自**只解析一次**状态快照，再从该快照取全部 buffer 指针。

- **BVH 节点按 48 字节值拷贝，被降级成 `memcpy`。**

  `BvhNode node = nodes[nodeIndex];` 每次节点访问都复制整个结构体。
  实测 `String.memcpy` 52,912 次 / 4.86 ms，与 `Aabb.Overlaps` 的 52,786 次
  （= 节点访问次数）几乎 1:1。

  改为 `ref readonly BvhNode node = ref nodes[nodeIndex];` 后不再产生拷贝
  （`BvhBuilder` 的遍历早就是 `ref`，只有这两个宿主侧入口是漏网的）。

### 说明

- 两项都只影响宿主侧查询热路径，**不改变任何查询语义与结果**。
- 查询总耗时的大头仍取决于**调用频率**：单次查询要遍历的节点数与查询盒覆盖范围相关，
  按实体、按帧逐个发查询的用法应自己做错峰与预算。

## [1.0.11] — 矩形 Gizmo 修成真正的矩形（蝴蝶结 → 四边环）

### Fixed

- **`Box2D` 的 Gizmo 画成了「蝴蝶结」（两个三角形拼成的漏斗），而不是矩形。**

  四个角用 `(i & 1, i & 2)` 生成，得到的是 `(-,-) (+,-) (-,+) (+,+)`；连成环就是
  「左下 → 右下 → 左上 → 右上」，两条对角线交叉，于是看到的是两个三角形。

  现在显式按逆时针环绕顺序 `(-,-) (+,-) (+,+) (-,+)` 排列，连线自然首尾相接成一个矩形。
  （1.0.9 只修了它的位置——世界中心被二次变换——并没有触及角点顺序。）

- **`Box`（3D）只画了 8 条棱，缺 4 条。**

  旧写法画了 4 条平行于 Z 的棱，再加 `-Z` 面上的 4 条（其中 2 条还重复画了两次），
  `+Z` 面上的 4 条一直没有。现在按「8 个角点里只差一个符号位的组合」遍历，
  恰好画满 12 条棱，且每条只画一次。

## [1.0.10] — 不再绘制「未参与碰撞的体」

### Fixed

- **调试视图会把不参与本帧碰撞的体也画出来，造成「明明有碰撞体、却不产生碰撞」的误导。**

  阵亡待重生的单位（示例中用 `CollisionBody.SetActive(false)` 停用）、被禁用的体，
  仍然在 body 池里、也仍然被编进宽相 BVH，但窄相 `IsPairEligible` 会用
  Enabled / Active 位把它们全部过滤掉，永远不会产生接触。

  而 `DrawShapes` / `DrawPairs` 之前直接遍历整个 body / pair 池，没有任何参与位过滤，
  于是这些体只残留一个空心轮廓；表现层又把它们的 Sprite 隐藏了，
  看上去就像场景里飘着一个「漏斗」（三角形 Polygon2D 单位最像）。

  现在默认跳过未参与碰撞的体；新增开关
  「绘制未参与碰撞的体（阵亡 / 未启用，灰色）」可把它们以灰色画出来。

### Added

- 调试窗口新增「未参与碰撞的体」计数，与「本帧接触体」并列，
  一眼就能看出有多少体在池里但不参与碰撞。

### Changed

- 形状 / pair 连线的绘制预算 `DrawLimit` 改为按「实际画出的条数」计。
  之前按池中下标计，被跳过的体会把预算吃掉，实际绘制数少于设定值。

## [1.0.9] — Gizmo 形状精度修复（缩放 / 双重变换 / 切线旋转）

### Fixed

- **Gizmo 尺寸错位：形状绘制忽略了 `BodyPose.Scale`。**

  窄相是按 `Params * Pose.Scale` 求解的，而形状绘制直接用原始参数，
  因此在用等比缩放表达碰撞体大小的场景里（例如 Samples Sample12），
  Gizmo 画出的圆 / 盒 / 胶囊比真实碰撞体大一截——调试时看到的形状与
  实际参与求解的形状不是一个大小。

  现在各形状按 `Pose.Scale` 缩放：`Circle` / `Sphere` 半径、`Box2D` / `Box`
  半范围、`Capsule2D` / `Capsule` 的半径与线段半长。
  `Polygon2D` 走 `TransformPoint`（本就含缩放），未受影响。
  注意 `TransformPoint` 带缩放而 `TransformDirection` 不带，因此每处缩放都显式写出。

- **`Box2D` Gizmo 位置双重变换。**

  四角计算把已经算好的世界中心又过了一次 `Pose.TransformPoint`（本地→世界），
  于是非原点实体的 2D 盒会画到完全错误的位置。现在只在本地角偏移上做
  旋转 + 缩放，再平移到世界中心。

- **`Capsule2D` 外公切线未随 pose 旋转**，非零旋转时切线画歪；
  侧向量现在同样过 `Pose.Rotation`。

## [1.0.8] — 接触高亮：发生碰撞的碰撞体与候选对画红

### Added

- **场景视图接触高亮**：新增开关「接触高亮（发生碰撞的体 / pair 画红）」（默认开）。

  开启后 `CollisionGizmoDrawer` 会把本帧**真正发生接触**的碰撞体画成红色，
  并把**窄相确认的候选 pair** 连线画成红色；仅宽相候选、实际并未相交的 pair 保持淡白。
  红色 = 求解实际拿到的接触，因此可以一眼看出「宽相给了多少候选、窄相留下了多少」。

  只宽相候选、无接触的体仍为青色；接触点（红球）与法线（黄线）图层不变。

- `CollisionWorldView` 新增两个公开只读访问器（供 Editor 与运行时烘焙消费）：

  | 访问器 | 内容 |
  | --- | --- |
  | `BodyContactFlagsPtr` | 每体 1 字节的当帧接触标志，与实体上 `CollisionState.HasContact` 同源，且不受 `MaxContacts` 截断影响 |
  | `PairContactCountPtr` | 每个候选 pair 的流形数（下标与 `CandidatePairPtr` 一一对应） |

  原先这两个 buffer 只有 internal 访问器，编辑器侧取不到。

### Changed

- 调试窗口新增「本帧接触体」计数，与红色高亮的数量对应，便于校验。
- 绘制颜色切换按「状态变化才换色」处理：体按 Morton 排序后接触标志成簸出现，
  因此 `Handles.color` 的切换次数接近接触簇数，而不是体数。

## [1.0.7] — 修复 Unity 宿主调度路径的 Job 容量字段漏赋值

### Fixed

- **宽相 / 窄相 / 接触事件的 Job 容量字段从未赋值，导致 Unity 下整条碰撞管线 fail-closed。**

  以下字段都是 Job 内部用于边界检查的容量上限，但调度侧（`CollisionBroadphaseSystem` /
  `CollisionNarrowphaseSystem` / `CollisionContactEventSystem`）从未给它们赋值，
  默认 0，于是每个 Job 都在第一道检查处提前返回：

  | Job | 漏赋值字段 | 后果 |
  | --- | --- | --- |
  | `PairCollectJob` | `PairCapacity` | 每个 leaf 都判 `limit = 0 - offset <= 0` → 全部写 -1 哨兵 → `DiagPairCapacityTruncated` → fail-closed 发布 **0 个候选 pair** |
  | `ContactCountJob` | `PairCapacity` | `pairIndex >= PairCapacity` 直接 return → **0 个接触流形** |
  | `ContactCollectJob` | `PairCapacity`, `ContactCapacity` | 同上，流形收集同样空转 |
  | `ContactFlagMarkJob` | `PairCapacity` | `limit` 被夹到 0 → `CollisionState` 永不置位 |
  | `ContactPairGatherJob` | `OutputCapacity` | `output >= OutputCapacity` 直接 return → **无 Enter / Stay / Exit 事件** |

  这 6 个字段现已全部按 `CollisionWorldView` 的实际容量赋值
  （`PairCapacity` / `ContactCapacity` / `ContactPairCapacity`）。

  纯 CLI 单测只覆盖 `BvhBuilder` / `NarrowphaseMath` / `BvhBuilder.CountHierarchyOverlaps`
  等裸指针纯逻辑，调度接线不在其覆盖范围内，因此该缺陷此前未被发现。
  本次由 Unity 宿主示例（ECS Framework Samples 的 Sample12「2D 碰撞大混战」，
  300 单位 + 子弹）实际运行暴露并修复：修复后每帧候选 pair / 接触流形 / 接触事件 / 击杀
  均正常产出，诊断槽全为 0。

## [1.0.6] — 场景 Gizmos 防花屏

### Fixed

- **Scene 视图偶发整屏花屏（绘制批次被坏坐标 / 非 Repaint 事件 GL 污染）。**

  `SceneView.duringSceneGui` 对 Layout / 鼠标事件同样触发，而 `SphereHandleCap` 等
  硬编码 `EventType.Repaint` 不区分当前事件，在非 Repaint 事件里发 GL 直接污染
  Scene 视图渲染。Gizmo 绘制器入口现已加 Repaint 门控。

  同时所有坐标入批前做有限性检查：BVH 内部节点以 `Aabb.Empty`（±Infinity）初始化，
  未合并完的节点、NaN 体姿 / 接触点一旦入批即整屏花屏——坏条按条丢弃，不再拖垮整批。

- **测试工程断引用修复**：`Ember.Collision.Tests` 不再引用已废弃的
  `Ember.Core.Package` 与 `libs/`，改用 `ProjectReference` 与 `Libs~`，
  IDE（Rider）打开不再报引用错误。

## [1.0.5] — 新增碰撞调试窗口与场景可视化

### Added

- **`Ember/Collision/调试窗口`：开关各图层，并显示宽相 / 窄相计数与溢出诊断。**

  计数面板列碰撞体数、分块数、BVH 内部节点、候选 pair、接触流形、检出接触、接触事件，
  以及容量溢出计数。body 数与 pair 数对不上说明宽相没覆盖；pair 有而接触为 0 说明
  窄相被过滤掉了；溢出计数非零说明容量不足、部分碰撞已被丢弃。

- **场景视图可视化**：

  | 图层 | 内容 |
  | --- | --- |
  | 碰撞体形状 | 按 body 池的 `BodyPose` + `Collider` 画：圆 / 2D 盒 / 2D 胶囊 / 2D 多边形 / 球 / 3D 盒 / 3D 胶囊 |
  | BVH | 内部节点包围盒，按深度渐隐 |
  | 宽相候选 pair | 按 pair 两端 body 的位置连线 |
  | 接触 | 接触点 + 法线（长度可调） |

  画的是求解用的同一份内存（直接读 body 池 / BVH / pair / contact 指针），
  不存在「调试副本与实际不一致」。

### Changed

- `CollisionWorldView` 新增公开只读访问器：`BvhNodePtr`、`CandidatePairPtr`、
  `ContactPtr`、`InternalNodeCount`。原先它们是 internal，编辑器侧取不到。
- `Ember.Collision.csproj` 排除 `Editor/**`（Editor 只由 Unity 编译，csproj 无 UnityEditor 引用）。

## [1.0.4] — 包仓库迁移到 ECS-Collision.git

### Changed

- **包仓库由 `Ember-Collision.git` 迁到 `ECS-Collision.git`，仓库根即 UPM 包根。**

  源码库与包库合并成一个仓库：源码库转公开直接当 UPM 包，独立的 DLL/源码副本包库删除。
  从此一份代码一条历史，不再有「同步到另一个仓库」这一步，也没有副本漂移的可能。

  布局按 Unity 包约定整理：

  | 旧 | 新 | 说明 |
  | --- | --- | --- |
  | `src/` | `Runtime/` | 配 `Ember.Collision.Runtime.asmdef` |
  | `libs/` | `Libs~/` | `~` 后缀让 Unity 忽略；否则 `Unity.Burst.dll` 会被当包内插件导入，与 `com.unity.burst` 撞名 |
  | `tests/` | `tests/`（加 asmdef） | `defineConstraints = UNITY_INCLUDE_TESTS`，否则测试代码会被编进包 |
  | 包库的 `package.json` / README / CHANGELOG / LICENSE | 仓库根 | — |

  包内每个资源都补了 `.meta`。Unity 对不可变包目录里没有 `.meta` 的资源**直接忽略**，
  只丢一条警告 —— 源码包资源上百个，漏一个就少一个文件。

  **消费方需改 manifest URL**：

  ```
  - https://github.com/NormanYUE/Ember-Collision.git
  + https://github.com/NormanYUE/ECS-Collision.git
  ```

  改完要删掉 `Library/PackageCache`，否则 UPM 不会重新解析。

- 依赖提升：com.ember.ecs 1.13.0、com.ember.core 2.1.4

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
