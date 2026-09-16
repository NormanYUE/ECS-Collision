using Ember.Core;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace Ember.Collision
{
    /// <summary>
    /// 采集作业（每个 Chunk 一次并行执行）：把散射在 Chunk 列里的碰撞数据
    /// 收拢为<b>稠密连续数组</b>，并把形状本地 AABB 写回 <c>BoundingVolume</c>。
    ///
    /// 写入 <c>BoundingVolume</c> 是本模块接入框架的关键接线：框架既有的
    /// <c>WorldBoundsSystem</c> 随即由它换算出 <c>WorldBounds</c>，
    /// 于是碰撞体<b>自动</b>参与视锥剔除与空间索引，无需任何额外代码。
    ///
    /// 稠密下标 = Chunk 顺序 + 行序，因此映射确定（同输入必得同序），
    /// 且可由 <see cref="ChunkInfo.Base"/> + 行号直接还原写回位置。
    /// </summary>
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public struct GatherJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<ChunkInfo> ChunkInfos;

        /// <summary>逐 Chunk 静态标志（Tag 为 Archetype 级，故每 Chunk 一字节即可）。</summary>
        [ReadOnly] public NativeArray<byte> ChunkStaticFlags;

        /// <summary>维度模式（决定无效轴的包围盒撑开）。</summary>
        public CollisionDimension Dimension;

        /// <summary>凸形状顶点池基址；0 表示空池。</summary>
        [NativeDisableUnsafePtrRestriction] public long VertexPtr;

        /// <summary>凸形状顶点池长度。</summary>
        public int VertexPoolLength;

        public NativeArray<Entity> BodyEntities;
        public NativeArray<Aabb> BodyBounds;
        public NativeArray<BodyPose> BodyPoses;
        public NativeArray<Collider> BodyColliders;
        public NativeArray<CollisionFilter> BodyFilters;
        public NativeArray<byte> BodyFlags;
        public NativeArray<int> BodyChunks;

        public void Execute(int chunkIndex)
        {
            unsafe
            {
                ChunkInfo info = ChunkInfos[chunkIndex];
                int count = info.Count;
                if (count <= 0) return;

                var colliders = (Collider*)info.ColliderPtr;
                var bodies = (CollisionBody*)info.BodyPtr;
                var filters = (CollisionFilter*)info.FilterPtr;
                var matrices = (float4x4*)info.LocalToWorldPtr;
                var volumes = (BoundingVolume*)info.BoundsPtr;
                var vertices = (float3*)VertexPtr;

                for (int row = 0; row < count; row++)
                {
                    int dense = info.Base + row;

                    Collider collider = colliders[row];
                    float4x4 localToWorld = matrices[row];

                    float3* vertexStart = null;
                    int vertexCount = collider.Params.VertexCount;
                    if (vertices != null && vertexCount > 0)
                    {
                        int start = collider.Params.VertexStart;
                        if (start >= 0 && start + vertexCount <= VertexPoolLength)
                            vertexStart = vertices + start;
                    }

                    ShapeBoundsMath.LocalBounds(
                        collider, Dimension, vertexStart, out float3 localCenter, out float3 localExtents);

                    volumes[row] = new BoundingVolume(localCenter, localExtents);

                    ShapeBoundsMath.ToWorldBounds(
                        localToWorld, localCenter, localExtents,
                        out float3 worldCenter, out float3 worldExtents);

                    Aabb bounds = Aabb.FromCenterExtents(worldCenter, worldExtents);
                    if (Dimension != CollisionDimension.XYZ)
                        bounds = Aabb.InflateInactiveAxis(bounds, Dimension);

                    CollisionBody body = bodies[row];

                    BodyEntities[dense] = body.Self;
                    BodyBounds[dense] = bounds;
                    BodyPoses[dense] = PoseMath.FromMatrix(localToWorld);
                    BodyColliders[dense] = collider;
                    BodyFilters[dense] = filters[row];
                    // 静态性有两个来源：框架的 Static 标签（Chunk 级）与用户显式写入的
                    // CollisionBody.Flags（实体级，可取反标签）。两者取并。
                    byte flags = body.Flags;
                    if (ChunkStaticFlags[chunkIndex] != 0)
                        flags |= CollisionBody.StaticBit;
                    BodyFlags[dense] = flags;
                    BodyChunks[dense] = chunkIndex;
                }
            }
        }
    }
}
