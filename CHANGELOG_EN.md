# Changelog

All notable changes to Ember Collision.

[中文](CHANGELOG.md)

## [1.0.12] — query hot path: resolve the singleton once, zero-copy node reads

### Performance

- **`OverlapAabb` / `RaycastAabb` re-resolved roughly ten properties on every query.**

  `CollisionWorldView.State` is a **by-value copy** of
  `World.GetComponent<CollisionWorld>(owner)`, and each `xxxPtr` accessor takes another copy of
  the whole state plus a `GetBuffer`. A single LBVH query touches about ten of them
  (`IsQueryReady / BodyCount / BvhNodePtr / SortedOrderPtr / BodyEntityPtr / BodyFilterPtr /
  BodyFlagPtr / TraversalStackPtr / LeafCapacity`), so the resolution cost outweighed the
  traversal itself.

  Measured under deep profile (126 queries/frame): each `get_*Ptr()` cost 2.6-3.6 us per call.
  Both entry points now resolve the state snapshot **once** and take every buffer pointer from
  that snapshot.

- **BVH nodes were copied by value (48 bytes), which lowered to `memcpy`.**

  `BvhNode node = nodes[nodeIndex];` copied the whole struct on every node visit. Measured:
  `String.memcpy` 52,912 calls / 4.86 ms against 52,786 `Aabb.Overlaps` calls (i.e. node
  visits) - almost exactly 1:1.

  Changed to `ref readonly BvhNode node = ref nodes[nodeIndex];`, which removes the copy.
  (`BvhBuilder`'s traversals already used `ref`; only these two host-side entry points were
  missed.)

### Notes

- Both changes only affect the host-side query hot path and **do not change any query semantics
  or results**.
- The dominant cost of a query is still **how often you call it**: the number of nodes a single
  query visits depends on how much of the world its box covers, so callers that fire one query
  per entity per frame should stagger them and budget them.

## [1.0.11] — rectangle gizmo is a real rectangle again (bowtie -> four-sided ring)

### Fixed

- **The `Box2D` gizmo was drawn as a bowtie (two triangles forming a funnel) instead of a
  rectangle.**

  The four corners were generated from `(i & 1, i & 2)`, which yields
  `(-,-) (+,-) (-,+) (+,+)`. Connecting those in a ring walks
  "bottom-left -> bottom-right -> top-left -> top-right", so the two diagonals cross and you see
  two triangles instead of a rectangle.

  The corners are now listed explicitly in counter-clockwise ring order
  `(-,-) (+,-) (+,+) (-,+)`, so consecutive edges close into a proper rectangle.
  (1.0.9 only fixed the position of this draw - the world centre was being transformed twice -
  and never touched the corner ordering.)

- **The 3D `Box` only drew 8 of its 12 edges.**

  The old code drew the 4 edges parallel to Z plus the 4 in-plane edges of the `-Z` face (two of
  them twice), and never drew the 4 in-plane edges of the `+Z` face. It now iterates the corner
  pairs that differ in exactly one sign bit, which yields exactly the 12 edges, each once.

## [1.0.10] — stop drawing bodies that do not participate in collision

### Fixed

- **The debug view drew bodies that take no part in the current frame's collision, which reads as
  "it has a collider but never collides".**

  A unit waiting to respawn (the sample calls `CollisionBody.SetActive(false)`) or a disabled body
  is still in the body pool and still gets inserted into the broad-phase BVH, but the narrow phase
  `IsPairEligible` filters all of them out via the Enabled / Active bits, so they can never produce
  a contact.

  `DrawShapes` / `DrawPairs` used to walk the whole body / pair pool with no participation filter,
  so those bodies left a hollow wireframe behind. The presentation layer hides their sprites,
  so it looked like a "funnel" floating in the scene (a triangular `Polygon2D` unit looks most
  like one).

  Non-participating bodies are now skipped by default; the new toggle
  "draw non-participating bodies (dead / disabled, grey)" draws them in grey.

### Added

- The debug window now reports a "non-participating bodies" count next to "contacting bodies this
  frame", so you can see at a glance how many bodies are in the pool but not colliding.

### Changed

- The `DrawLimit` budget for shapes and pair lines is now counted in **lines actually drawn**.
  Previously it counted pool indices, so skipped bodies consumed the budget and fewer items were
  drawn than configured.

## [1.0.9] — Gizmo shape accuracy fixes (scale / double transform / tangent rotation)

### Fixed

- **Gizmo sizes were wrong: shape drawing ignored `BodyPose.Scale`.**

  The narrow phase solves with `Params * Pose.Scale`, but the shape drawing used the raw
  parameters. In scenes that express collider size through a uniform `LocalToWorld` scale
  (for example ECS Framework Samples Sample12), the drawn circle / box / capsule was visibly
  larger than the real collider, so the debug view disagreed with the shape actually being
  solved.

  Shapes are now scaled by `Pose.Scale`: the radius of `Circle` / `Sphere`, the half extents of
  `Box2D` / `Box`, and the radius plus segment half-height of `Capsule2D` / `Capsule`.
  `Polygon2D` goes through `TransformPoint`, which already applies the scale, so it was
  unaffected. Note that `TransformPoint` applies scale while `TransformDirection` does not, so
  every scaling site is written out explicitly.

- **`Box2D` gizmo double-transformed its position.**

  The corner calculation pushed an already-computed world centre through
  `Pose.TransformPoint` (local -> world) a second time, so 2D boxes on entities away from the
  origin were drawn in a completely wrong place. Only the local corner offsets are rotated and
  scaled now, then translated to the world centre.

- **`Capsule2D` outer tangents did not rotate with the pose**, so they were drawn skewed under
  non-zero rotation; the side vector now goes through `Pose.Rotation` as well.

## [1.0.8] — Contact highlight: draw colliding bodies and pairs in red

### Added

- **Scene-view contact highlight**: new toggle "contact highlight (bodies / pairs in red)"
  (on by default).

  When enabled, `CollisionGizmoDrawer` draws bodies that **actually have a contact this frame**
  in red, and draws **narrow-phase-confirmed candidate pairs** in red as well. Pairs that were
  only broad-phase candidates and never truly intersected stay faint white, and non-contacting
  bodies stay cyan. Contact points (red spheres) and normals (yellow lines) are unchanged.

  Red therefore means "the contact the solver actually got", so you can see at a glance how many
  candidates the broad phase produced versus how many survived the narrow phase.

- Two new public read-only accessors on `CollisionWorldView` (for the Editor and runtime baking):

  | Accessor | Contents |
  | --- | --- |
  | `BodyContactFlagsPtr` | one byte of per-body contact flag for the frame; same source as `CollisionState.HasContact` on the entity, and unaffected by `MaxContacts` truncation |
  | `PairContactCountPtr` | manifold count per candidate pair (indices match `CandidatePairPtr`) |

  Both buffers previously only had internal accessors, so the Editor could not reach them.

### Changed

- The debug window now reports a "contacting bodies this frame" count that matches the red
  highlights, making it easy to cross-check.
- Colour switching is now "only on state change": bodies are Morton-sorted so contact flags appear
  in clusters, which keeps `Handles.color` switches near the number of clusters rather than the
  number of bodies.

## [1.0.7] — Fix unassigned job capacity fields in the Unity host path

### Fixed

- **Capacity fields on the broadphase / narrowphase / contact-event jobs were never
  assigned, so the whole collision pipeline failed closed under Unity.**

  All of the fields below are boundary-check limits used inside the jobs, but the
  schedulers (`CollisionBroadphaseSystem` / `CollisionNarrowphaseSystem` /
  `CollisionContactEventSystem`) never set them, leaving them at 0 so that every job
  bailed out at its first guard:

  | Job | Unassigned field | Effect |
  | --- | --- | --- |
  | `PairCollectJob` | `PairCapacity` | every leaf computed `limit = 0 - offset <= 0` → all leaves wrote the -1 sentinel → `DiagPairCapacityTruncated` → fail-closed published **0 candidate pairs** |
  | `ContactCountJob` | `PairCapacity` | `pairIndex >= PairCapacity` returned immediately → **0 contact manifolds** |
  | `ContactCollectJob` | `PairCapacity`, `ContactCapacity` | same, manifold collection never ran |
  | `ContactFlagMarkJob` | `PairCapacity` | `limit` clamped to 0 → `CollisionState` never set |
  | `ContactPairGatherJob` | `OutputCapacity` | `output >= OutputCapacity` returned immediately → **no Enter / Stay / Exit events** |

  All six fields are now assigned from the real `CollisionWorldView` capacities
  (`PairCapacity` / `ContactCapacity` / `ContactPairCapacity`).

  The pure CLI tests only cover raw-pointer logic such as `BvhBuilder` /
  `NarrowphaseMath`, not the scheduling wiring, which is why this went unnoticed.
  It was found by actually running a Unity host sample (ECS Framework Samples,
  Sample12 "2D collision brawl", 300 units plus bullets): after the fix the frame
  produces candidate pairs, contact manifolds, contact events and kills, with all
  diagnostic slots reading 0.

## [1.0.6] — Scene gizmo anti-corruption fix

### Fixed

- **Intermittent full-screen garbling in the Scene view (draw batches polluted by bad
  coordinates and GL emitted on non-Repaint events).**

  `SceneView.duringSceneGui` fires for Layout / mouse events as well, and the hardcoded
  `EventType.Repaint` in `SphereHandleCap` and friends does not check the current event —
  GL emitted outside the repaint pass corrupts the Scene view. The gizmo drawer now gates
  on Repaint at the entry point.

  All coordinates are finite-checked before entering a batch: BVH internal nodes are
  initialized with `Aabb.Empty` (±Infinity), and unmerged nodes or NaN poses / contact
  points would garble the whole screen — bad entries are now skipped per item instead of
  poisoning the batch.

- **Broken references in the test project**: `Ember.Collision.Tests` no longer references
  the retired `Ember.Core.Package` or `libs/`; it uses `ProjectReference` and `Libs~`,
  so opening the repo in an IDE (Rider) no longer reports reference errors.

## [1.0.5] — Collision debug window and scene visualization

### Added

- **`Ember/Collision/调试窗口` (Debug Window): per-layer toggles plus broadphase/narrowphase counts
  and overflow diagnostics.**

  The panel lists body count, chunk count, BVH internal nodes, candidate pairs, contact manifolds,
  detected contacts, contact events, and the capacity-overflow counter. Bodies but no pairs means
  the broadphase is not covering them; pairs but no contacts means the narrowphase filtered them;
  a non-zero overflow counter means capacity is short and collisions are being dropped.

- **Scene view visualization**:

  | Layer | Content |
  | --- | --- |
  | Collider shapes | drawn from the body pool's `BodyPose` + `Collider`: circle / box2D / capsule2D / polygon2D / sphere / box / capsule |
  | BVH | internal node bounds, fading with depth |
  | Candidate pairs | a line between the two bodies of each pair |
  | Contacts | contact points plus normals (adjustable length) |

  Drawn straight from the same memory the solver uses (body pool / BVH / pair / contact pointers),
  so there is no debug copy that can drift from the real state.

### Changed

- `CollisionWorldView` gained public read-only accessors: `BvhNodePtr`, `CandidatePairPtr`,
  `ContactPtr`, `InternalNodeCount`. They were internal, which put them out of reach for editor code.
- `Ember.Collision.csproj` now excludes `Editor/**` (editor code is compiled by Unity only; the csproj
  has no UnityEditor reference).

## [1.0.4] — Package repository moved to ECS-Collision.git

### Changed

- **The package repository moved from `Ember-Collision.git` to `ECS-Collision.git`, and the repository
  root is now the UPM package root.**

  The source repository and the package repository merged into one: the source repository went
  public and is now itself the UPM package, while the separate package repository was deleted.
  One codebase, one history — no sync step, no chance of the copy drifting.

  The layout follows Unity's package conventions:

  | Before | After | Why |
  | --- | --- | --- |
  | `src/` | `Runtime/` | pairs with `Ember.Collision.Runtime.asmdef` |
  | `libs/` | `Libs~/` | the `~` suffix makes Unity ignore it; otherwise `Unity.Burst.dll` is imported as a package plugin and collides with `com.unity.burst` |
  | `tests/` | `tests/` (now with an asmdef) | `defineConstraints = UNITY_INCLUDE_TESTS`, otherwise the tests get compiled into the package |
  | the package repo's `package.json` / README / CHANGELOG / LICENSE | repository root | — |

  Every asset now has a `.meta`. Unity **silently ignores** assets without one inside an immutable
  package folder, emitting only a console warning — a source package has hundreds of assets, and
  each missing `.meta` is a missing file.

  **Consumers must update their manifest URL**:

  ```
  - https://github.com/NormanYUE/Ember-Collision.git
  + https://github.com/NormanYUE/ECS-Collision.git
  ```

  Delete `Library/PackageCache` afterwards, or UPM will not re-resolve.

- Dependencies raised: com.ember.ecs 1.13.0, com.ember.core 2.1.4

## [1.0.3] — Ships as a source package (no more precompiled DLL)

### Changed

- **The package now carries `Runtime/**/*.cs` instead of `Runtime/*.dll`.**

  **Why**: `[BurstCompile]` inside a precompiled DLL is never processed by Unity's Burst ILPP —
  that pipeline only runs over assemblies Unity itself compiles. In practice 22 jobs (Collision 19,
  Navigation 1, Core 2) reported `not a known Burst entry point` at runtime and fell back to the
  managed path. As a source package Unity compiles them, and the pipeline applies.

  **Also fixed by this**:
  - Unity-side symbols such as `#if ENABLE_UNITY_COLLECTIONS_CHECKS` are **actually defined** when
    compiling source, so a block inside such a guard can no longer be dead code (that is exactly how
    `Ember.Collision` shipped a broken pipeline)
  - No more hand-maintained `.meta` files in the package repo (the root cause of the 0.3.1 / 0.3.2
    incidents)
  - Consumers get readable, debuggable, steppable source

  **No consumer change required**: the package URL is unchanged and so is the public API.

- `Runtime/Ember.collision.Runtime.asmdef` now points `precompiledReferences` at **`Ember.dll`**
  (the framework still ships precompiled) instead of its own DLL. `references` explicitly lists the
  Unity-side dependencies (`Unity.Collections` / `Unity.Mathematics` / `Unity.Burst`) and the
  upstream Ember assemblies.
- Added the missing `com.unity.collections` dependency — a DLL package compiled against dotnet-side
  `libs/Unity.Collections.dll` never had to declare it; a source package must.
- Dependencies raised: com.ember.ecs 1.13.0, com.ember.core 2.1.3

### Notes

- Sync tool: `Ember.Framework/tools/deploy-package-sources.py` (source repo → package repo, with
  deterministic `.meta` generation). See the root `CLAUDE.md` for the release flow.
- Not verified: Unity runtime. The cross-package precompiled reference (`Ember.dll`) in the asmdef
  still needs to resolve inside Unity.

## [1.0.2] — Fix: DiagnosticFlags never had its length set

### Fixed

- **`CollisionBroadphaseSystem` no longer throws `DiagnosticFlagPtr: buffer too small — requested
  8 x Int32, buffer holds 0`.**

  The root cause is not a "growth expression mismatch" — **the growth call does not exist at all**.
  `World.CreateBuffer<T>(initialCapacity)` sets only the **capacity**; the logical length is **0**
  (`BufferStore.CreateBuffer` writes `Length = 0, Capacity = capacity`). Of the 35 buffers
  `EnsureInitialized` creates, 34 are later given a length by `Grow`; `DiagnosticFlags` never was,
  so its span stayed empty while the broadphase reads it every frame.

  `EnsureCapacity` now calls
  `Grow<int>(ref state.DiagnosticFlags, CollisionWorld.DiagnosticSlotCount);`.

### Changed

- **All 35 buffer creation sites switched to `World.CreateSizedBuffer<T>(n)`** (new in Ember 1.13.0),
  which sets the length as it creates. That removes the whole "created but never given a length"
  class — previously 34 buffers were covered by `Grow` and one was missed, held together by hand.
- Dependencies raised to `com.ember.ecs` 1.13.0 and `com.ember.core` 2.1.2.

### Notes

- Not verified: Unity runtime. Compilation and all 227 algorithm tests pass on the CLI.

## [1.0.1] — Fix: no more silent null pointers when a buffer is too small

### Fixed

- **`NativePointer<T>` now throws instead of returning `0L` when it cannot supply the memory.**

  When 1.0.0 introduced raw pointers, the accessors returned a silent `0` on a capacity
  shortfall — a bad design: the job then dereferences `(T*)0`, which on the managed path degrades
  into a **stack-less** `NullReferenceException`, with the `NativeArray` indexer that used to
  catch this already gone.

  It now throws `InvalidOperationException`, using `[CallerMemberName]` to name the accessor and
  report the requested and actual lengths, e.g.:

  ```
  CollisionWorldView.ContactOffsetPtr: buffer too small — requested 2049 x Int32,
  buffer holds 2048. The growth expression in EnsureCapacity / EnsurePairDependentCapacity
  does not match the length this accessor asks for.
  ```

  A length of 0 is still a legitimate request (no bodies / no pairs this frame) and still returns 0.

- **`ContactOffsets` / `ContactCounts` growth now matches what the accessors ask for.**

  This was a **reachable** null pointer: `Grow` rounds up to a power of two, and those two buffers
  were grown to the *requested* capacity while the accessors ask for `PairCapacity` (the **actual**
  length of `CandidatePairs`). The two disagree whenever `m_PredictedPairs` is not a power of two —
  with pairCount = 600, `m_PredictedPairs` = 1200, so `ContactOffsets` is grown to
  `nextPow2(1201)` = 2048 while the accessor requests `PairCapacity + 1` =
  `nextPow2(1200) + 1` = 2049 > 2048.

  Both are now grown from `CandidatePairs`' actual length, so they align by construction.

### Notes

- Consumer `Ember.Navigation` 0.2.4 follows.

## [1.0.0] — Fix: jobs use raw pointers; snapshot and query entry points change shape

### Fixed

- **The collision pipeline no longer spams errors every frame inside a real Unity world.** The
  three reported failures (`GatherJob.BodyEntities has not been assigned or constructed`,
  `ContactFlagClearJob.BodyContactFlags ...`, and the NRE in the contact-event system) share one
  root cause: **the `NativeArray` safety handle was never set.**

  `NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray` assigns only
  `m_Buffer` / `m_Length` / `m_AllocatorLabel` / `m_MinIndex` / `m_MaxIndex` — **never
  `m_Safety`** — so the handle is `default`. Two consequences: as a job container field it is
  rejected outright by the schedule-time container validation, and indexing it on the main thread
  dereferences a null `versionNode`.

  The code that would set the handle is **dead code**: the `SetAtomicSafetyHandle` call in
  `NativeBufferUtil` sits inside `#if ENABLE_UNITY_COLLECTIONS_CHECKS`, and that symbol is defined
  in no csproj or props file in this repository — nor does `dotnet build` define it.
  Decompiling the shipped `Ember.Collision.dll` confirms it directly: it contains
  `ConvertExistingDataToNativeArray` but neither `SetAtomicSafetyHandle` nor `GetTempMemoryHandle`.

  Neither way of supplying a handle works: `GetTempMemoryHandle()` returns the handle for the
  **currently active temp memory scope** (its companion API `IsTempMemoryHandle` documents exactly
  that), which is not a "valid for this frame" guarantee; and `AtomicSafetyHandle.Create()` must be
  paired with `Release()`, while this module's resource model is "buffers are released with
  `World.Dispose()` — no Dispose needed", so handles would drag in a whole lifecycle.

  The fix therefore follows what `Ember.Framework`'s chunk jobs already do: **put the raw pointer
  in a `[NativeDisableUnsafePtrRestriction] public long` field and cast inside the job.** This very
  view class already had a `VertexPointer` in exactly that shape.

### Changed

- **The body snapshot on `CollisionWorldView` is now raw pointers**, with a `Ptr` suffix:
  `BodyPoses` → `BodyPosesPtr`, `BodyColliders` → `BodyCollidersPtr`,
  `BodyFilters` → `BodyFiltersPtr`, `BodyFlags` → `BodyFlagsPtr`,
  `VertexPool` → `VertexPoolPtr`. Element counts come from `BodyCount` / `VertexCount`.
- **`World.TryGetContacts` / `World.TryGetContactEvents` are now
  `out long ptr, out int count`.**
- The 36 internal `*Array` view properties became `*Ptr` as well; new
  `PairCapacity` / `ContactCapacity` / `ContactPairCapacity` / `ContactEventCapacity` /
  `ChunkCount` — with raw pointers the job side can no longer read `NativeArray.Length`, so
  capacities must be passed explicitly.
- `NativeBufferUtil.AsNativeArray` is marked `[Obsolete]` (kept so already-compiled consumers
  still bind); `NativeBufferUtil.AsPointer` is new.

### Breaking

Two public signatures changed. It still ships as a fix because **the `NativeArray` the old
signatures returned never had a valid handle — indexing it always failed**, so it was not a usable
interface.

### Notes

- Consumer `Ember.Navigation` 0.2.3 follows (its `NavRuntimeBake` and
  `NavDynamicObstacleSystem` take raw pointers; the snapshot copy now uses `UnsafeUtility.MemCpy`).
- Not verified: Unity runtime. What the CLI does verify is compilation, all 227 algorithm tests,
  and a full rebuild against the assemblies a Unity 2022 project actually resolves
  (Collections 2.4.3 / Burst 1.8.21 / Mathematics 1.3.2).

## [0.3.2] — Fix: unregistered component type when systems are constructed before the World

### Fixed
- **Registering `CollisionSystemGroup` in the documented assembly order no longer throws
  `unregistered component type`.**

  `SystemTicker.Register` **constructs systems immediately**, and in the documented example that happens
  before `ECSManager.Start()` — and therefore before the `World` exists. Component type registration,
  however, only happened inside the `World` constructor. Any system whose field initializer builds an
  `EntityQuery` (`ComponentMask.With<T>()` reads `ComponentTypeRegistry` on the spot) therefore threw on
  the first run in a fresh AppDomain.

  Affected: `CollisionSetupSystem`, `CollisionBroadphaseSystem`, `CollisionNarrowphaseSystem`.
  Their query fields lost the initializer and `readonly` and are now built in `OnCreate()`, which
  `SystemTicker.Init` calls after the `World` is constructed; `BuildAccess` runs right after, and
  `DeclareAccess` does not depend on those fields.

### Changed
- Dependency `com.ember.ecs` raised from 1.11.0 to 1.12.0 and `com.ember.core` from 2.1.0 to 2.1.1
  (both fix the same defect; UPM resolves exact versions, so not following the bump means not getting
  the fix).

### Notes
- The framework side (1.12.0) covers the `SystemTicker` assembly path and the package side no longer
  depends on a global side effect at construction time. Both changed deliberately.

## [0.3.1] — Missing .meta files added (Unity ignores un-meta'd assets in immutable packages)

### Fixed

- Added `.meta` files for every asset in the package (six documents/config files plus the `Runtime`
  folder, the DLL and the asmdef).

  Unity **silently ignores** assets that lack a `.meta` inside an immutable package folder
  (`Library/PackageCache`), so `Ember.Collision.dll` was never loaded and any assembly referencing it
  failed with `Unable to resolve reference 'Ember.Collision'` — which in turn stopped downstream
  packages such as Ember.Navigation from loading at all.

  This was a packaging omission. **The DLL contents are unchanged**; only the metadata Unity requires
  has been added.

## [0.3.0] — Precise Ray Queries (navigation dependency delivery)

### Added

- **`ShapeQuery.Raycast`** (N3): closest intersection of a ray with a single shape, covering
  all 7 `ShapeType`s. `direction` need not be normalized (the implementation normalizes it);
  returns the distance along the ray, the world-space hit point, and the unit outward normal
  pointing back at the ray. Burst-compatible pure static functions, no managed allocation.
  - Well-defined boundaries: an origin inside or on the shape (signed distance ≤ 0) hits
    immediately (`distance = 0`); a zero direction or a negative `maxDistance` misses;
    `maxDistance == 0` hits only for an inside origin; tangency (zero discriminant) counts as
    a hit; on a miss `distance = 0`, `point = origin`, `normal` is zeroed.
  - 2D shapes are treated as prisms extending infinitely along the inactive axis and solved in
    the `CollisionDimension` plane; `point` is the true point on the ray at that parameter
    (including the inactive-axis component). A ray perpendicular to the plane whose projection
    falls outside the shape misses.
  - Capsules use the cylinder quadratic plus two hemispherical caps — the half of a cap sphere
    that coincides with the cylinder is not surface; boxes use a slab test in local space
    (reusing `CollisionQueryMath.TryRayAabb`); convex polygons test every edge and take the
    closest hit.
  - Polygons require the vertex-pool overload; non-polygon shapes may call the convenience
    overload that omits the pool.

### Changed

- Dependency `com.ember.ecs` raised from 1.10.1 to 1.11.0 and `com.ember.core` from 2.0.0 to 2.1.0.
- Test suite grew to 229 tests: 227 pass on CLI; 2 Unity-host stub tests run in the Unity Test Runner.

## [0.2.0] — Geometry Queries and Body Snapshot (navigation dependency delivery)

### Added

- **`ShapeQuery.ClosestPoint`** (N1): shape → point closest-point / signed-distance query
  covering all 7 `ShapeType`s. Return value is positive (outside) / negative (inside,
  -penetration depth) / zero (on surface); `normal` is the unit vector from the closest
  point toward the query point; 2D shapes are lifted to 3D per `CollisionDimension`
  semantics (XY / XZ). Burst-compatible pure static functions, no managed allocation.
  Polygon shapes require the vertex-pool overload; degenerate inputs (zero-radius sphere,
  zero-extent box, degenerate capsule, polygon with < 3 vertices) have well-defined
  behavior.
- **Public body snapshot on `CollisionWorldView`** (N2): read-only `NativeArray` views
  `BodyPoses` / `BodyColliders` / `BodyFilters` / `BodyFlags` / `VertexPool` for
  runtime-bake consumers. Valid only after the broadphase has published
  (`IsQueryReady == true`), read-only, must not be cached across frames; reading earlier
  throws `InvalidOperationException`. Zero allocation.

### Perf

- `CollisionWorldView.Grow` switched from per-element Add loop to a single
  `ResizeBuffer` call (requires Ember framework ≥ 1.11.0).

## [0.1.0] — Initial release

### Added

- Broadphase: Morton encoding, radix sort and BVH build, all as Burst-compiled jobs.
- Narrowphase: candidate pair tests producing `ContactManifold` / `ContactPoint`.
- Events: `CollisionContactEventSystem` emits enter, stay and exit events by diffing
  contact state against the previous frame.
- Data types: `Collider`, `CollisionBody`, `BodyPose`, `ShapeParams`, `ShapeType`,
  `CollisionDimension`, `CollisionFilter`, `CollisionConfig`, `CollisionState`,
  `Aabb`, `PairKey`, `CollisionRaycastHit`.
- System group: `CollisionSystemGroup` bundles the Setup, Broadphase, Narrowphase and
  ContactEvent systems.
- Query entry points: the `World` extensions `EnsureCollisionWorld`, `TryGetContacts`,
  `TryGetContactEvents`, `OverlapAabb`, `RaycastAabb`, plus the `CollisionWorldView` tree
  operations.
- All state lives in World-managed buffers and is released with `World.Dispose()`.
