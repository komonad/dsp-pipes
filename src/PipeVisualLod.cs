using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using FluidPipes.API;
using UnityEngine;
using UnityEngine.Rendering;

namespace FluidPipes
{
    internal static partial class PipeVisuals
    {
        private const float DistantChunkSize = 128;
        private const float FullDetailRadiusPixels = 24;
        private const float MiddleDetailRadiusPixels = 6;
        private const float MergeTangentDotThreshold = -0.995f;
        private static bool distantMembershipDirty = true;
        private const double StructureBuildBudgetMilliseconds = 2;
        private static int urgentBuildUntilFrame;
        private static readonly LinkedList<DetailBuild> detailBuildQueue =
            new LinkedList<DetailBuild>();
        internal static bool StructureMeshesReady
        {
            get
            {
                foreach (var entry in distantChunks)
                {
                    if (!entry.Value.Meshes.DetailMeshes.ContainsKey(2))
                    {
                        return false;
                    }
                    foreach (var child in entry.Value.Children)
                    {
                        if (!child.Value.DetailMeshes.ContainsKey(1))
                        {
                            return false;
                        }
                    }
                }
                return true;
            }
        }
        internal static long ShadowTriangles { get; private set; }
        internal static int ShadowDrawCalls { get; private set; }

        private sealed class DetailBuild
        {
            internal Chunk Chunk;
            internal int Level;
            internal IEnumerator<object> Work;
            internal bool Completed;
            internal LinkedListNode<DetailBuild> QueueNode;
        }

        private static DetailBuild RequestDetailBuild(
            PlanetFactory factory,
            ChunkKey key,
            Chunk chunk,
            int level,
            bool urgent = false
        )
        {
            if (urgent)
            {
                urgentBuildUntilFrame = Time.frameCount + 2;
            }
            if (!chunk.PendingDetails.TryGetValue(level, out var pending))
            {
                pending = new DetailBuild
                {
                    Chunk = chunk,
                    Level = level,
                    Work = BuildDetailMeshes(factory, key, chunk, level).GetEnumerator(),
                };
                chunk.PendingDetails.Add(level, pending);
                pending.QueueNode = urgent
                    ? detailBuildQueue.AddFirst(pending)
                    : detailBuildQueue.AddLast(pending);
            }
            else if (urgent && pending.QueueNode?.List != null)
            {
                detailBuildQueue.Remove(pending.QueueNode);
                detailBuildQueue.AddFirst(pending.QueueNode);
            }
            return pending;
        }

        private static void CompleteDetailBuild(DetailBuild pending)
        {
            pending.Completed = true;
            pending.Work.Dispose();
            pending.Chunk.PendingDetails.Remove(pending.Level);
        }

        internal static void PrewarmStructureMeshes(PlanetFactory factory, Camera camera)
        {
            if (factory?.cargoTraffic == null || camera == null)
            {
                return;
            }
            Initialize(factory);
            foreach (var entry in RenderChunks(camera)) { }
            foreach (var entry in distantChunks)
            {
                if (!entry.Value.Meshes.DetailMeshes.ContainsKey(2))
                {
                    RequestDetailBuild(factory, entry.Key, entry.Value.Meshes, 2);
                }
            }
            foreach (var entry in chunks)
            {
                if (
                    PipeTypeRegistry.Get(entry.Key.ItemId)?.Definition.Behavior
                        == PipeBehavior.Structure
                    && !entry.Value.DetailMeshes.ContainsKey(1)
                )
                {
                    RequestDetailBuild(factory, entry.Key, entry.Value, 1);
                }
            }
            var clock = Stopwatch.StartNew();
            double budget =
                Time.frameCount <= urgentBuildUntilFrame ? 4 : StructureBuildBudgetMilliseconds;
            while (detailBuildQueue.Count > 0 && clock.Elapsed.TotalMilliseconds < budget)
            {
                var pending = detailBuildQueue.First.Value;
                if (pending.Completed)
                {
                    detailBuildQueue.RemoveFirst();
                    continue;
                }
                if (!pending.Work.MoveNext())
                {
                    CompleteDetailBuild(pending);
                    detailBuildQueue.RemoveFirst();
                }
            }
        }

        private static readonly Dictionary<ChunkKey, DistantChunk> distantChunks =
            new Dictionary<ChunkKey, DistantChunk>();

        private sealed class DistantChunk
        {
            internal Chunk Meshes = new Chunk { Dirty = false };
            internal readonly Dictionary<int, NodeShape> Shapes = new Dictionary<int, NodeShape>();
            internal readonly List<KeyValuePair<ChunkKey, Chunk>> Children =
                new List<KeyValuePair<ChunkKey, Chunk>>();
        }

        private static void ClearDistantChunks()
        {
            foreach (var group in distantChunks.Values)
            {
                FreeDetailMeshes(group.Meshes);
            }
            distantChunks.Clear();
            detailBuildQueue.Clear();
            distantMembershipDirty = true;
        }

        private static IEnumerable<KeyValuePair<ChunkKey, Chunk>> RenderChunks(Camera camera)
        {
            if (distantMembershipDirty)
            {
                var grouped = new Dictionary<ChunkKey, DistantChunk>();
                foreach (var entry in chunks)
                {
                    if (
                        PipeTypeRegistry.Get(entry.Key.ItemId)?.Definition.Behavior
                        != PipeBehavior.Structure
                    )
                    {
                        continue;
                    }
                    Vector3 center = Origin(entry.Key) + Vector3.one * StructureChunkSize * 0.5f;
                    var cell = new Vector3Int(
                        Mathf.FloorToInt(center.x / DistantChunkSize),
                        Mathf.FloorToInt(center.y / DistantChunkSize),
                        Mathf.FloorToInt(center.z / DistantChunkSize)
                    );
                    var key = new ChunkKey(cell, entry.Key.ItemId, entry.Key.Group, true);
                    if (!grouped.TryGetValue(key, out var group))
                    {
                        grouped.Add(key, group = new DistantChunk());
                    }
                    group.Children.Add(entry);
                    foreach (int entityId in entry.Value.Entities)
                    {
                        var shape = GetNodeGeometry(current, entityId).Shape;
                        group.Meshes.Entities.Add(entityId);
                        group.Meshes.Representative = entityId;
                        group.Shapes.Add(entityId, shape);
                    }
                }
                foreach (var entry in grouped)
                {
                    if (
                        distantChunks.TryGetValue(entry.Key, out var previous)
                        && SameShapes(previous.Shapes, entry.Value.Shapes)
                    )
                    {
                        entry.Value.Meshes = previous.Meshes;
                        distantChunks.Remove(entry.Key);
                    }
                    else
                    {
                        foreach (var child in entry.Value.Children)
                        {
                            FreeDetailMeshes(child.Value);
                        }
                    }
                }
                foreach (var previous in distantChunks.Values)
                {
                    FreeDetailMeshes(previous.Meshes);
                }
                distantChunks.Clear();
                foreach (var entry in grouped)
                {
                    distantChunks.Add(entry.Key, entry.Value);
                }
                distantMembershipDirty = false;
            }
            foreach (var entry in chunks)
            {
                if (
                    PipeTypeRegistry.Get(entry.Key.ItemId)?.Definition.Behavior
                    != PipeBehavior.Structure
                )
                {
                    yield return entry;
                }
            }
            foreach (var entry in distantChunks)
            {
                var group = entry.Value;
                var dimensions = PipeTypeRegistry.Geometry(entry.Key.ItemId);
                var bounds = new Bounds(
                    Origin(entry.Key) + Vector3.one * DistantChunkSize * 0.5f,
                    Vector3.one
                        * (DistantChunkSize + 8 + dimensions.CenterHeight + dimensions.Radius * 2)
                );
                if (group.Meshes.HasBounds)
                {
                    bounds = group.Meshes.Bounds;
                    bounds.center += Origin(entry.Key);
                }
                if (
                    StructureDetailLevel(
                        camera,
                        bounds,
                        dimensions,
                        PipeTypeRegistry.Get(entry.Key.ItemId).Definition
                    ) == 2
                )
                {
                    yield return new KeyValuePair<ChunkKey, Chunk>(entry.Key, group.Meshes);
                }
                else
                {
                    foreach (var child in group.Children)
                    {
                        yield return child;
                    }
                }
            }
        }

        private sealed class StructureLodMeshes
        {
            internal Mesh Wall;
            internal Mesh Core;
        }

        private static void DrawStructureShadows(PlanetFactory factory, Camera camera, int itemId)
        {
            ShadowTriangles = 0;
            ShadowDrawCalls = 0;
            foreach (var entry in distantChunks)
            {
                if (
                    (itemId > 0 && entry.Key.ItemId != itemId)
                    || !entry.Value.Meshes.DetailMeshes.TryGetValue(2, out var meshes)
                )
                {
                    continue;
                }
                var surfaces = PipeMaterialLibrary.Get(entry.Key.ItemId);
                Matrix4x4 transform = Matrix4x4.Translate(Origin(entry.Key));
                if (surfaces.WallMode == PipeWallMode.Opaque)
                {
                    SubmitStructureShadow(meshes.Wall, surfaces.Wall, 0, transform, camera);
                }
                SubmitStructureShadow(meshes.Wall, surfaces.Fittings, 1, transform, camera);
                SubmitStructureShadow(meshes.Core, surfaces.Core, 0, transform, camera);
                SubmitStructureShadow(meshes.Core, surfaces.Fittings, 1, transform, camera);
                SubmitStructureShadow(meshes.Core, surfaces.Detail, 4, transform, camera);
            }
        }

        private static void SubmitStructureShadow(
            Mesh mesh,
            Material material,
            int submesh,
            Matrix4x4 transform,
            Camera camera
        )
        {
            if (
                mesh == null
                || material == null
                || submesh >= mesh.subMeshCount
                || mesh.GetIndexCount(submesh) == 0
                || material.renderQueue >= 3000
                || material.GetTag("RenderType", false) == "Transparent"
            )
            {
                return;
            }
            // 阴影使用固定细节级别，并由光源的剔除结果确定投影范围。
            ShadowTriangles += (long)mesh.GetIndexCount(submesh) / 3;
            ShadowDrawCalls++;
            Graphics.DrawMesh(
                mesh,
                transform,
                material,
                0,
                camera,
                submesh,
                null,
                ShadowCastingMode.ShadowsOnly,
                false
            );
        }

        private static bool SameShapes(
            Dictionary<int, NodeShape> first,
            Dictionary<int, NodeShape> second
        )
        {
            // 形状快照在几何变化时替换；相同引用保留已生成的远景网格。
            if (first.Count != second.Count)
            {
                return false;
            }
            foreach (var entry in first)
            {
                if (
                    !second.TryGetValue(entry.Key, out var shape)
                    || !ReferenceEquals(entry.Value, shape)
                )
                {
                    return false;
                }
            }
            return true;
        }

        private static int StructureDetailLevel(
            Camera camera,
            Bounds bounds,
            PipeGeometryProfile dimensions,
            PipeTypeDefinition definition
        )
        {
            if (definition?.Behavior != PipeBehavior.Structure)
            {
                return 0;
            }
            float pixelsPerUnit;
            if (camera.orthographic)
            {
                pixelsPerUnit = camera.pixelHeight / (2 * camera.orthographicSize);
            }
            else
            {
                float depth =
                    Vector3.Dot(bounds.center - camera.transform.position, camera.transform.forward)
                    - bounds.extents.magnitude;
                if (depth <= 0)
                {
                    return 0;
                }
                pixelsPerUnit =
                    camera.pixelHeight
                    / (2 * depth * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f));
            }
            float projectedRadius = dimensions.Radius * pixelsPerUnit;
            return projectedRadius >= FullDetailRadiusPixels ? 0
                : projectedRadius >= MiddleDetailRadiusPixels ? 1
                : 2;
        }

        private static void FreeDetailMeshes(Chunk chunk)
        {
            foreach (var pending in chunk.PendingDetails.Values)
            {
                pending.Completed = true;
                pending.Work.Dispose();
            }
            chunk.PendingDetails.Clear();
            foreach (var meshes in chunk.DetailMeshes.Values)
            {
                if (meshes.Wall != null)
                {
                    UnityEngine.Object.Destroy(meshes.Wall);
                }
                if (meshes.Core != null)
                {
                    UnityEngine.Object.Destroy(meshes.Core);
                }
            }
            chunk.DetailMeshes.Clear();
        }

        private static StructureLodMeshes GetDetailMeshes(
            PlanetFactory factory,
            ChunkKey key,
            Chunk chunk,
            int detailLevel
        )
        {
            if (!chunk.DetailMeshes.TryGetValue(detailLevel, out var cached))
            {
                var pending = RequestDetailBuild(factory, key, chunk, detailLevel);
                while (pending.Work.MoveNext()) { }
                CompleteDetailBuild(pending);
                cached = chunk.DetailMeshes[detailLevel];
            }
            return cached;
        }

        private static IEnumerable<object> BuildDetailMeshes(
            PlanetFactory factory,
            ChunkKey key,
            Chunk chunk,
            int detailLevel
        )
        {
            Vector3 origin = Origin(key);
            var walls = new PipeMesh();
            var core = new PipeMesh();
            var definition = PipeTypeRegistry.Get(key.ItemId).Definition;
            var eligible = new HashSet<int>();
            foreach (int entityId in chunk.Entities)
            {
                chunk.Representative = entityId;
                walls.Append(
                    NodeMesh(factory, entityId, false, detailLevel),
                    factory.entityPool[entityId].pos - origin
                );
                var shape = GetNodeGeometry(factory, entityId).Shape;
                if (
                    shape.Ends.Count == 2
                    && shape.MachineSockets.Count == 0
                    && (
                        definition.DirectionMode == PipeDirectionMode.Undirected
                        || (
                            PipeTypes.GetIncomingNeighbors(factory, entityId).Length == 1
                            && PipeTypes.GetOutgoingNeighbors(factory, entityId).Length == 1
                        )
                    )
                    && Vector3.Dot(
                        (shape.Ends[0] - shape.Center).normalized,
                        (shape.Ends[1] - shape.Center).normalized
                    ) < MergeTangentDotThreshold
                )
                {
                    eligible.Add(entityId);
                }
                else if (definition.Appearance?.HasCore == true)
                {
                    core.Append(
                        NodeMesh(factory, entityId, true, detailLevel),
                        factory.entityPool[entityId].pos - origin
                    );
                }
                yield return null;
            }
            if (definition.Appearance?.HasCore == true)
            {
                var adjacency = new Dictionary<int, List<int>>();
                foreach (int entityId in eligible)
                {
                    var neighbors = new List<int>();
                    foreach (int neighbor in PipeTypes.GetNeighbors(factory, entityId))
                    {
                        if (eligible.Contains(neighbor))
                        {
                            neighbors.Add(neighbor);
                        }
                    }
                    adjacency.Add(entityId, neighbors);
                }
                var visited = new HashSet<int>();
                foreach (int entityId in eligible)
                {
                    if (adjacency[entityId].Count < 2)
                    {
                        foreach (
                            var step in AppendInteriorChain(
                                factory,
                                key,
                                core,
                                adjacency,
                                visited,
                                entityId,
                                detailLevel
                            )
                        )
                        {
                            yield return step;
                        }
                    }
                }
                foreach (int entityId in eligible)
                {
                    foreach (
                        var step in AppendInteriorChain(
                            factory,
                            key,
                            core,
                            adjacency,
                            visited,
                            entityId,
                            detailLevel
                        )
                    )
                    {
                        yield return step;
                    }
                }
            }
            var cached = new StructureLodMeshes
            {
                Wall =
                    walls.VertexCount == 0
                        ? null
                        : walls.Build("FluidPipes.StructureLOD.Wall", true),
                Core =
                    core.VertexCount == 0
                        ? null
                        : core.Build("FluidPipes.StructureLOD.Core", doubleSidedOpaque: false),
            };
            chunk.DetailMeshes.Add(detailLevel, cached);
            if (cached.Wall != null || cached.Core != null)
            {
                chunk.Bounds = cached.Wall != null ? cached.Wall.bounds : cached.Core.bounds;
                if (cached.Wall != null && cached.Core != null)
                {
                    chunk.Bounds.Encapsulate(cached.Core.bounds);
                }
                chunk.HasBounds = true;
                chunk.Bounds.Expand(0.2f);
            }
            yield break;
        }

        private static IEnumerable<object> AppendInteriorChain(
            PlanetFactory factory,
            ChunkKey key,
            PipeMesh target,
            Dictionary<int, List<int>> adjacency,
            HashSet<int> visited,
            int start,
            int detailLevel
        )
        {
            if (visited.Contains(start))
            {
                yield break;
            }
            var chain = new List<int>();
            int currentEntity = start;
            while (visited.Add(currentEntity))
            {
                chain.Add(currentEntity);
                int next = 0;
                foreach (int neighbor in adjacency[currentEntity])
                {
                    if (!visited.Contains(neighbor))
                    {
                        next = neighbor;
                        break;
                    }
                }
                if (next == 0)
                {
                    break;
                }
                currentEntity = next;
            }
            var definition = PipeTypeRegistry.Get(key.ItemId).Definition;
            var appearance = definition.Appearance;
            if (appearance.Interior == null)
            {
                foreach (int entityId in chain)
                {
                    target.Append(
                        NodeMesh(factory, entityId, true, detailLevel),
                        factory.entityPool[entityId].pos - Origin(key)
                    );
                }
                yield break;
            }
            var first = GetNodeGeometry(factory, chain[0]).Shape;
            if (chain.Count > 1 && first.InteriorOutputEnd >= 0)
            {
                Vector3 nextCenter =
                    GetNodeGeometry(factory, chain[1]).Shape.Center
                    + factory.entityPool[chain[1]].pos;
                Vector3 direction = nextCenter - (first.Center + factory.entityPool[chain[0]].pos);
                if (Vector3.Dot(first.Ends[first.InteriorOutputEnd] - first.Center, direction) < 0)
                {
                    chain.Reverse();
                    first = GetNodeGeometry(factory, chain[0]).Shape;
                }
            }
            Vector3 origin = Origin(key);
            var points = new List<Vector3>();
            var axes = new List<Vector3>();
            var frames = new List<Vector3>();
            for (int index = 0; index < chain.Count; index++)
            {
                int entityId = chain[index];
                var shape = GetNodeGeometry(factory, entityId).Shape;
                Vector3 offset = factory.entityPool[entityId].pos - origin;
                if (index == 0 || index == chain.Count - 1)
                {
                    int otherEntity = chain.Count == 1 ? 0 : chain[index == 0 ? 1 : index - 1];
                    Vector3 towardOther =
                        otherEntity == 0
                            ? Vector3.zero
                            : GetNodeGeometry(factory, otherEntity).Shape.Center
                                + factory.entityPool[otherEntity].pos
                                - (shape.Center + factory.entityPool[entityId].pos);
                    int outside =
                        otherEntity == 0
                            ? (shape.InteriorOutputEnd >= 0 ? 1 - shape.InteriorOutputEnd : 0)
                            : (Vector3.Dot(shape.Ends[0] - shape.Ends[1], towardOther) < 0 ? 0 : 1);
                    if (index == 0)
                    {
                        points.Add(shape.Ends[outside] + offset);
                        axes.Add(-shape.EndAxes[outside]);
                        frames.Add(shape.EndUps[outside]);
                    }
                    points.Add(shape.Center + offset);
                    axes.Add(
                        (shape.Ends[1 - outside] - shape.Ends[outside]).normalized
                            * (index == 0 ? 1 : -1)
                    );
                    frames.Add(shape.Up);
                    if (index == chain.Count - 1)
                    {
                        if (chain.Count == 1)
                        {
                            outside = 1 - outside;
                        }
                        points.Add(shape.Ends[outside] + offset);
                        axes.Add(shape.EndAxes[outside]);
                        frames.Add(shape.EndUps[outside]);
                    }
                }
                else
                {
                    points.Add(shape.Center + offset);
                    Vector3 direction =
                        factory.entityPool[chain[index + 1]].pos
                        - factory.entityPool[chain[index - 1]].pos;
                    axes.Add(direction.normalized);
                    frames.Add(shape.Up);
                }
            }
            var builder = new PipeMesh(
                definition.Geometry.Radius,
                false,
                PipeMaterialLibrary.TextureCoordinate(key.ItemId, factory),
                origin,
                definition.Geometry,
                appearance.TextureCoordinatePeriod,
                patternFactory: factory,
                detailLevel: detailLevel
            );
            foreach (var member in appearance.Interior.Members)
            {
                builder.InteriorPath(
                    points,
                    first.Up,
                    axes,
                    frames,
                    new PipeInteriorProfile(new[] { member }),
                    false,
                    false
                );
                yield return null;
            }
            target.Append(builder, Vector3.zero);
        }
    }
}
