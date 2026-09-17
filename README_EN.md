# Ember Collision Manual

[中文](README.md)

Collision package for the Ember ECS framework. The broadphase builds a BVH over
Morton-coded radix-sorted bounds, the narrowphase produces contact manifolds, and
all data lives in World-managed buffers released with `World.Dispose()`.

## Installation

This package depends on `com.ember.ecs` and `com.ember.core`. Install through the
Unity Package Manager:

1. Open `Packages/manifest.json` in your project.
2. Add the dependencies (versions follow the actual release):

```json
{
  "dependencies": {
    "com.ember.ecs": "1.10.1",
    "com.ember.core": "2.0.0",
    "com.ember.collision": "0.1.0"
  }
}
```

3. When installing through a Git URL: UPM does not resolve transitive Git
   dependencies, so declare `com.ember.ecs` and `com.ember.core` explicitly.

Requires Unity 2022.3 or newer.

## Systems

`CollisionSystemGroup` drives the following systems in order. Register the group
with a World and it takes part in dependency-graph scheduling automatically:

| System | Responsibility |
| --- | --- |
| `CollisionSetupSystem` | Syncs collider data and initializes intermediate buffers |
| `CollisionBroadphaseSystem` | Morton encoding, radix sort and BVH build; emits candidate pairs |
| `CollisionNarrowphaseSystem` | Tests candidate pairs and writes `ContactManifold` |
| `CollisionContactEventSystem` | Diffs contact state against the previous frame; emits enter/stay/exit events |

## Data

Components and configuration live in the `Ember.Collision` namespace:

- `Collider`, `CollisionBody`, `BodyPose`, `ShapeParams` — colliders and shapes
- `ShapeType`, `CollisionDimension`, `CollisionFilter` — shape kinds, dimensionality and filtering
- `CollisionConfig` — global switches (broadphase mode, contact capacity, and so on)
- `ContactManifold`, `ContactPoint`, `ContactEvent`, `PairKey` — contact and event data
- `CollisionState`, `Aabb`, `CollisionRaycastHit` — state, bounds and query results

## Query entry points

Extension methods on `World` (`CollisionWorldExtensions`):

```csharp
world.EnsureCollisionWorld();                                  // build the tree and register the singleton
if (world.TryGetContacts(out long contacts, out int contactCount)) { /* this frame's manifolds */ }
if (world.TryGetContactEvents(out long events, out int eventCount)) { /* this frame's enter/exit events */ }
world.OverlapAabb(aabb, ref results);                          // AABB overlap query
world.RaycastAabb(origin, direction, maxDistance, out var hit); // ray query
world.GetCandidatePairCount();                                 // broadphase candidate count
world.GetContactCount();                                       // contact count
```

`CollisionWorldView` exposes tree insert, update, remove and query operations.
`WorldBounds` and `LocalToWorld` from `Ember.Core` feed the broadphase directly.

## Geometry queries and snapshot

`ShapeQuery` offers two groups of Burst-compatible pure static functions: shape → point
closest-point queries, and ray queries.

```csharp
// shape → point closest point / signed distance (positive outside, negative inside)
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

// body snapshot (runtime-bake input): valid only after the broadphase has
// published; read-only, must not be cached across frames
var view = world.GetCollisionWorld();
if (view.IsQueryReady)
{
    unsafe
    {
        var poses      = (BodyPose*)view.BodyPosesPtr;        // length == view.BodyCount
        var colliders  = (Collider*)view.BodyCollidersPtr;
        var filters    = (CollisionFilter*)view.BodyFiltersPtr;
        var flags      = (byte*)view.BodyFlagsPtr;            // includes Static bit
        var vertexPool = (float3*)view.VertexPoolPtr;         // Polygon2D vertices
    }
}
```

> The snapshot and both query entry points return **raw pointers**, not `NativeArray`.
> A `NativeArray` built over foreign memory has a `default` safety handle
> (`ConvertExistingDataToNativeArray` never assigns `m_Safety`): as a job container field it is
> rejected by the schedule-time container validation, and indexing it on the main thread
> dereferences a null handle node. Raw pointers have neither problem, and match the shape the
> framework's chunk jobs already use. Element counts come from `view.BodyCount`,
> `view.VertexCount` and the query methods' `out count`.

Snapshot accessors throw `InvalidOperationException` when `IsQueryReady == false`.
`ShapeQuery` functions are Burst-compatible pure static functions with no managed allocation.

## Notes

- Versions use plain `a.b.c`; breaking changes bump `a`, features bump `b`, fixes and
  optimizations bump `c`.
- This repository holds build artifacts only; the source lives in a private repository.
