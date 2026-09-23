# Ember Collision Manual

[中文](README.md)

Collision package for the Ember ECS framework. The broadphase builds a Burst-compiled
BVH over Morton-coded radix-sorted bounds, the narrowphase produces contact manifolds,
and all data lives in World-managed buffers released with `World.Dispose()`.

- **2D / 3D unified**: the dimension comes from `CollisionConfig.Dimension`, with no
  separate code paths
- **7 shapes**: Sphere / Box / Capsule / Circle / Box2D / Capsule2D / Polygon2D
- **Zero allocation on hot paths**: broadphase, narrowphase and contact events run
  entirely in Burst jobs, with scratch memory owned by the World

## Getting the package

Add it to your project's `Packages/manifest.json` as a Git URL:

```json
{
  "dependencies": {
    "com.ember.collision": "https://github.com/NormanYUE/ECS-Collision.git"
  }
}
```

Append `#<tag-or-branch>` to pin a revision: `#main` tracks the production branch (the
default), `#develop` tracks the test branch. Omitting it follows the repository's default
branch.

## Getting the dependencies

This package depends on two Ember packages and three Unity registry packages:

| Dependency | Source | Declare explicitly? |
| --- | --- | --- |
| `com.ember.ecs` (framework) | `https://github.com/NormanYUE/Ember-Framework.git` | **Yes** |
| `com.ember.core` | `https://github.com/NormanYUE/ECS-Core.git` | **Yes** |
| `com.unity.mathematics` | Unity registry | No, resolved automatically |
| `com.unity.burst` | Unity registry | No, resolved automatically |
| `com.unity.collections` | Unity registry | No, resolved automatically |

**UPM does not resolve transitive Git dependencies.** The package's own `package.json`
declares version strings (such as `"com.ember.core": "2.1.6"`), and UPM looks those up in
the registry — where these packages do not exist — so resolution fails. Both Ember
dependencies must therefore be declared in your manifest:

```json
{
  "dependencies": {
    "com.ember.ecs": "https://github.com/NormanYUE/Ember-Framework.git",
    "com.ember.core": "https://github.com/NormanYUE/ECS-Core.git",
    "com.ember.collision": "https://github.com/NormanYUE/ECS-Collision.git"
  }
}
```

The dependency direction is `Ember.Collision` → `Ember.Core` → `Ember.Framework`.
UPM resolves exact versions, so align them bottom-up: update the framework first, then
Core, then this package — otherwise you will not pick up fixes from the layer below.

Requires Unity 2022.3 or newer.

## Adding collision to an entity

### 1. Register the system group

```csharp
manager.GetTicker(updateIdx).Register<CollisionSystemGroup>();
```

`CollisionSystemGroup` registers the whole collision pipeline in one call; the
registration order inside the group is the pipeline order (the dependency graph then
layers systems by read/write conflicts):

| System | Responsibility |
| --- | --- |
| `CollisionSetupSystem` | Fills in the collision components for entities carrying `Collider` (once per entity) |
| `CollisionBroadphaseSystem` | Morton encoding, radix sort and BVH build; emits candidate pairs |
| `CollisionNarrowphaseSystem` | Tests candidate pairs and writes `ContactManifold` |
| `CollisionContactEventSystem` | Diffs contact state against the previous frame; emits enter / stay / exit events |

If you also want frustum culling and the spatial index, register the framework's spatial
group as well (order does not matter — the dependency graph layers it automatically):

```csharp
manager.GetTicker(updateIdx).Register<SpatialSystemGroup>();
```

### 2. Attach `Collider` and a pose

```csharp
var entity = world.CreateEntity();

world.AddComponent(entity, Collider.Sphere(0.5f));

var l2w = float4x4.TRS(position, rotation, new float3(1f));
world.AddComponent(entity, new LocalToWorld { Value = l2w });
```

`Collider` exposes factory methods for all 7 shapes:

```csharp
Collider.Sphere(radius, center)                        // 3D
Collider.Box(halfExtents, center)                      // 3D box
Collider.Capsule(radius, halfHeight, axis, center)     // 3D capsule, axis: 0=X 1=Y 2=Z
Collider.Circle(radius, center)                        // 2D
Collider.Box2D(halfExtents, center)                    // 2D box
Collider.Capsule2D(radius, halfHeight, axis, center)   // 2D capsule
Collider.Polygon2D(vertexStart, vertexCount, center)   // 2D convex polygon, vertices from the vertex pool
```

The remaining components (`CollisionBody` / `CollisionFilter` / `CollisionState` /
`BoundingVolume`) are filled in by `CollisionSetupSystem` — no manual registration.
Adding `Collider` is all it takes to enter the broadphase.

> **`LocalToWorld` is the pose input and is maintained by the caller.** Neither the
> framework nor `Ember.Core` writes it: write it once for static bodies, and once per
> frame after moving/integrating for dynamic bodies (or let your own transform system
> maintain it). The gather job's query requires `Collider` and `LocalToWorld` together,
> so an entity without `LocalToWorld` never enters the collision pipeline.

### 3. Optional: filters and global configuration

**Filters** — without one, `CollisionFilter.Default` is filled in
(`BelongsTo = 1`, `CollidesWith` = everything):

```csharp
world.AddComponent(entity, CollisionFilter.SingleLayer(2));
// Or fully specified: belongs to layer 2, collides only with layer 3
world.AddComponent(entity, new CollisionFilter(belongsTo: 1u << 2, collidesWith: 1u << 3));
```

**Global configuration** — without the singleton, `CollisionConfig.Default` applies
(3D, broadphase range ±1000, static-static pairs skipped):

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

> Once the singleton exists it is used as-is (the `Default` fallback only applies when
> the singleton is absent), so fill in every field you care about — or assign
> `CollisionConfig.Default` first and then tweak. A zero-valued singleton means the `XY`
> dimension and a zero-sized range, and collision will not work.

Remaining `CollisionConfig` fields: `MaxTreeDepth`, `LeafCapacity`, `SolverIterations`,
`ContactSlop`, `PositionCorrectionRate`, `SleepLinearThreshold`, `SleepTimeThreshold`,
`SkipStaticPairs`, `SkipInactivePairs`.

### 4. Static bodies, disabled entities and runtime toggles

```csharp
// Static body: add Ember.Core's Static tag. The broadphase decides static membership
// per chunk; with SkipStaticPairs on, static-static pairs are skipped outright
world.AddComponent(entity, new Static());

// Out of collision for the frame: add the Disabled tag (excluded by the broadphase query)
world.AddComponent(entity, new Disabled());

// Runtime per-frame toggle (keeps the components, flips the active bit only)
ref var body = ref world.GetComponent<CollisionBody>(entity);
body.SetActive(false);          // does not participate this frame
bool active   = body.IsActive;
bool enabled  = body.IsEnabled;
bool isStatic = body.IsStatic;
```

Entities carrying the `Prefab` tag never enter the collision pipeline.

### 5. Components and data structures

| Type | Description |
| --- | --- |
| `Collider`, `ShapeParams` | Shape body and parameters (radius, half extents, axis, vertex-pool range) |
| `BodyPose` | Position / rotation / scale, decomposed from `LocalToWorld` by the broadphase |
| `CollisionBody` | Runtime state mirror (static bit, enabled bit, active bit, self handle) |
| `ShapeType`, `CollisionDimension`, `CollisionFilter` | Shape kinds, dimensionality, layer filtering |
| `CollisionConfig` | Global configuration singleton |
| `ContactManifold`, `ContactPoint` | Contact manifold and contact points |
| `ContactEvent`, `ContactEventPhase`, `PairKey` | Contact events and pair keys |
| `CollisionState` | Per-entity contact history (`HasContact` / `HadContact` / `EnteredContact` / `ExitedContact`) |
| `Aabb`, `CollisionRaycastHit` | Bounds and ray hit results |

## Consuming collision

All entry points below are extension methods on `World` (`CollisionWorldExtensions`).
Except for `ShapeQuery`, they require the current frame's broadphase results to be
published. Returned pointers/views are **valid for the current frame only**.

### Contact manifolds

```csharp
if (world.TryGetContacts(out long contactsPtr, out int contactCount))
{
    unsafe
    {
        var manifolds = (ContactManifold*)contactsPtr;
        for (int i = 0; i < contactCount; i++)
        {
            ref readonly ContactManifold m = ref manifolds[i];
            // m.A / m.B are normalized to ascending entity slots; m.Normal is the contact normal
            for (int p = 0; p < m.Count; p++)      // Count <= ContactManifold.Capacity (4)
            {
                ContactPoint point = m.GetPoint(p); // Position / Separation
            }
        }
    }
}

int contactCount2 = world.GetContactCount();          // same count, without the data
int pairCount     = world.GetCandidatePairCount();    // broadphase candidate pairs
```

### Contact events

```csharp
if (world.TryGetContactEvents(out long eventsPtr, out int eventCount))
{
    unsafe
    {
        var events = (ContactEvent*)eventsPtr;
        for (int i = 0; i < eventCount; i++)
        {
            ref readonly ContactEvent e = ref events[i];
            // e.A / e.B, e.Phase in { ContactEventPhase.Enter, Stay, Exit }
        }
    }
}
```

### AABB overlap and ray queries

```csharp
using var results = new NativeList<Entity>(Allocator.Temp);
world.OverlapAabb(queryBounds, belongsToMask, ref results);   // belongsToMask = 0 means any layer

if (world.RaycastAabb(origin, direction, maxDistance, belongsToMask, out CollisionRaycastHit hit))
{
    // hit.Entity / hit.Distance / hit.Position / hit.Normal
}
```

Both traverse the current frame's LBVH, so call them after the broadphase has published
this frame.

### Geometry queries and snapshot

`ShapeQuery` offers two groups of Burst-compatible pure static functions: shape → point
closest-point queries, and ray queries. They do not depend on a `World`, so they are
usable inside jobs for prediction, aiming and editor tooling.

```csharp
// shape -> point closest point / signed distance (positive outside, negative inside),
// covering all 7 shapes
float d = ShapeQuery.ClosestPoint(collider, pose, dimension, point,
    out float3 closest, out float3 normal);
// Polygon2D requires the vertex-pool overload
float d2 = ShapeQuery.ClosestPoint(collider, pose, dimension, point,
    vertexPoolPtr, vertexPoolLength, out closest, out normal);

// ray vs a single shape: distance along the ray, world hit point, unit outward
// normal pointing back at the ray. direction need not be normalized; an origin
// inside or on the shape reports distance = 0
bool hit = ShapeQuery.Raycast(collider, pose, dimension, origin, direction, maxDistance,
    out float distance, out float3 point, out float3 normal);
// Polygon2D requires the vertex-pool overload
bool hit2 = ShapeQuery.Raycast(collider, pose, dimension, origin, direction, maxDistance,
    vertexPoolPtr, vertexPoolLength, out distance, out point, out normal);

// Polygon2D vertex pool: append once at initialization, returns the start index
int start = world.AppendVertices(new[] { v0, v1, v2, v3 });
world.AddComponent(entity, Collider.Polygon2D(start, 4));
```

> The vertex pool grows for the lifetime of the `World`, and
> `ShapeParams.VertexStart` is an index into it. Append during initialization / body
> creation only — never per frame.

Body snapshot (runtime bake input): valid only after the broadphase has published;
read-only, must not be cached across frames.

```csharp
var view = world.GetCollisionWorld();
if (view.IsQueryReady)
{
    unsafe
    {
        var poses      = (BodyPose*)view.BodyPosesPtr;        // length == view.BodyCount
        var colliders  = (Collider*)view.BodyCollidersPtr;
        var filters    = (CollisionFilter*)view.BodyFiltersPtr;
        var flags      = (byte*)view.BodyFlagsPtr;            // includes the Static bit
        var vertexPool = (float3*)view.VertexPoolPtr;         // Polygon2D vertices, length == view.VertexCount
    }
}
```

Snapshot accessors throw `InvalidOperationException` when `IsQueryReady == false`.

> The snapshot and both query entry points return **raw pointers**, not `NativeArray`.
> A `NativeArray` built over foreign memory has a `default` safety handle
> (`ConvertExistingDataToNativeArray` never assigns `m_Safety`): as a job container field
> it is rejected by the schedule-time container validation, and indexing it on the main
> thread dereferences a null handle node. Raw pointers have neither problem, and match the
> shape the framework's chunk jobs already use. Element counts come from `view.BodyCount`,
> `view.VertexCount` and the query methods' `out count`. Consuming assemblies must enable
> `unsafe`.

### Memory and lifetime

- Collision scratch memory is owned by the `World`: **no `Dispose` needed**, it is released
  with `World.Dispose()`.
- Views and pointers are **valid for the current frame only** — any buffer growth relocates
  memory, so do not hold a reference across a structural change.
- `CollisionWorldView` additionally exposes capacity pre-allocation and diagnostic counters
  (`EnsureCapacity`, `OverflowCount`, and so on) for growing buffers up front, or for
  diagnosing "capacity too small, some collisions were dropped".
