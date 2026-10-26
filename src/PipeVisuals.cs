using System;
using System.Collections.Generic;
using FluidPipes.API;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace FluidPipes
{
    internal static partial class PipeVisuals
    {
        private const float ChunkSize = 16;
        private const float FluidOpacity = 0.68f;

        private readonly struct ChunkKey : IEquatable<ChunkKey>
        {
            internal readonly Vector3Int Cell;
            internal readonly int ItemId;
            internal readonly int Group;

            internal ChunkKey(Vector3Int cell, int itemId, int group)
            {
                Cell = cell;
                ItemId = itemId;
                Group = group;
            }

            public bool Equals(ChunkKey other) =>
                Cell == other.Cell && ItemId == other.ItemId && Group == other.Group;

            public override bool Equals(object other) => other is ChunkKey key && Equals(key);

            public override int GetHashCode() =>
                unchecked((Cell.GetHashCode() * 397 ^ ItemId) * 397 ^ Group);
        }

        private sealed class Chunk
        {
            public readonly HashSet<int> Entities = new HashSet<int>();
            public bool Dirty = true;
            public Mesh Mesh;
            public Mesh CoreMesh;
            public int Representative;
            public int MeshedEntities;
            public long LiquidVersion = -1;
            public readonly Dictionary<int, LiquidGroup> Liquids =
                new Dictionary<int, LiquidGroup>();
        }

        private sealed class LiquidGroup
        {
            public int Representative;
            public Mesh Mesh;
            public readonly List<int> Entities = new List<int>();
        }

        private sealed class NodeGeometry
        {
            public int ItemId;
            public PipeMesh Wall;
            public PipeMesh Liquid;
            public NodeShape Shape;
            public bool Dirty = true;
        }

        private sealed class NodeShape
        {
            public Vector3 Position;
            public Vector3 Center;
            public Vector3 Up;
            public Vector3 Forward;
            public List<Vector3> Ends;
            public List<Vector3> EndAxes;
            public List<Vector3> EndUps;
            public List<bool> Collars;
            public List<float> EndPeerRadiusRatios;
            public int InteriorOutputEnd;
            public readonly List<MachineSocketShape> MachineSockets =
                new List<MachineSocketShape>();

            public bool Matches(NodeShape other)
            {
                if (
                    other == null
                    || !Position.Equals(other.Position)
                    || !Center.Equals(other.Center)
                    || !Up.Equals(other.Up)
                    || InteriorOutputEnd != other.InteriorOutputEnd
                    || Ends.Count != other.Ends.Count
                    || MachineSockets.Count != other.MachineSockets.Count
                )
                {
                    return false;
                }
                for (int index = 0; index < MachineSockets.Count; index++)
                {
                    if (!MachineSockets[index].Equals(other.MachineSockets[index]))
                    {
                        return false;
                    }
                }

                if (Ends.Count == 0 && !Forward.Equals(other.Forward))
                {
                    return false;
                }

                for (int index = 0; index < Ends.Count; index++)
                {
                    if (
                        !Ends[index].Equals(other.Ends[index])
                        || !EndAxes[index].Equals(other.EndAxes[index])
                        || !EndUps[index].Equals(other.EndUps[index])
                        || Collars[index] != other.Collars[index]
                        || EndPeerRadiusRatios[index] != other.EndPeerRadiusRatios[index]
                    )
                    {
                        return false;
                    }
                }

                return true;
            }

            public void AddTo(PipeMesh builder, bool liquid = false)
            {
                builder.Node(
                    Center,
                    Up,
                    Forward,
                    Ends,
                    Collars,
                    EndAxes,
                    EndUps,
                    EndPeerRadiusRatios
                );
                if (!liquid)
                {
                    foreach (var socket in MachineSockets)
                    {
                        builder.MachinePort(socket.Anchor, socket.Orientation, socket.OutletHeight);
                    }
                }
            }

            public void AddInteriorTo(PipeMesh builder, PipeInteriorProfile profile)
            {
                builder.InteriorNode(
                    Center,
                    Up,
                    Forward,
                    Ends,
                    EndAxes,
                    EndUps,
                    profile,
                    InteriorOutputEnd
                );
            }
        }

        private readonly struct MachineSocketShape
        {
            public readonly Vector3 Anchor;
            public readonly Quaternion Orientation;
            public readonly float OutletHeight;

            public MachineSocketShape(Vector3 anchor, Quaternion orientation, float outletHeight)
            {
                Anchor = anchor;
                Orientation = orientation;
                OutletHeight = outletHeight;
            }
        }

        private static PlanetFactory current;
        private static readonly object colliderChangesLock = new object();
        private static int colliderChangesEpoch;
        private static Dictionary<PlanetFactory, HashSet<int>> colliderChanges =
            new Dictionary<PlanetFactory, HashSet<int>>();
        private static readonly Dictionary<ChunkKey, Chunk> chunks =
            new Dictionary<ChunkKey, Chunk>();
        private static readonly Dictionary<int, ChunkKey> keys = new Dictionary<int, ChunkKey>();
        private static readonly Dictionary<int, NodeGeometry> geometry =
            new Dictionary<int, NodeGeometry>();
        private static readonly Plane[] planes = new Plane[6];
        private static Material body;
        private static Material joint;
        private static Material machinePanel;
        private static Material machineShell;
        private static Material innerBody;
        private static readonly Dictionary<int, Material> liquids = new Dictionary<int, Material>();
        private static readonly Dictionary<int, Texture2D> liquidTints =
            new Dictionary<int, Texture2D>();
        private static readonly Dictionary<int, Color> liquidColors = new Dictionary<int, Color>();
        private static Cubemap steelEnvironment;
        private static MaterialPropertyBlock steelLighting;
        private static readonly MaterialPropertyBlock effectProperties =
            new MaterialPropertyBlock();
        public static int Rebuilds { get; private set; }
        public static int NodeRebuilds { get; private set; }
        public static int LiquidRebuilds { get; private set; }
        public static Material JointMaterial
        {
            get
            {
                var material = BodyMaterial;
                return joint;
            }
        }
        internal static Material InnerWallMaterial
        {
            get
            {
                var initialized = BodyMaterial;
                return innerBody;
            }
        }
        internal static Material MachinePanelMaterial
        {
            get
            {
                var initialized = BodyMaterial;
                return machinePanel;
            }
        }
        internal static Material MachineShellMaterial
        {
            get
            {
                var initialized = BodyMaterial;
                return machineShell;
            }
        }
        public static Material BodyMaterial
        {
            get
            {
                if (body == null)
                {
                    var shader = Shader.Find("Standard") ?? Shader.Find("Legacy Shaders/Diffuse");
                    if (shader == null)
                    {
                        throw new InvalidOperationException("Pipe surface shader is unavailable.");
                    }

                    var glass = TankMaterial(true);
                    var glassShader =
                        Shader.Find("VF Shaders/Forward/PBR Standard Glass")
                        ?? Shader.Find("Standard (Specular setup)")
                        ?? shader;
                    body = new Material(glassShader);
                    body.name = "FluidPipes.GlassWall";
                    body.color = new Color(0.90f, 0.96f, 0.98f, 0.22f);
                    body.SetTexture("_MainTex", Texture2D.whiteTexture);
                    Transparent(body, 3000);
                    if (body.HasProperty("_SpecColor"))
                    {
                        body.SetColor(
                            "_SpecColor",
                            glass?.GetColor("_SpecularColor") ?? new Color(0.198f, 0.198f, 0.198f)
                        );
                    }

                    if (body.HasProperty("_RimColor"))
                    {
                        var rimColor =
                            glass?.GetColor("_RimColor") ?? new Color(0.604f, 0.846f, 0.859f);
                        rimColor.a = 0.3f;
                        body.SetColor("_RimColor", rimColor);
                    }
                    if (body.HasProperty("_SpecularColor"))
                    {
                        body.SetColor("_SpecularColor", new Color(0.26f, 0.27f, 0.28f));
                    }

                    if (body.HasProperty("_MetallicMultiplier"))
                    {
                        body.SetFloat("_MetallicMultiplier", 0);
                    }

                    if (body.HasProperty("_SmoothMultiplier"))
                    {
                        body.SetFloat("_SmoothMultiplier", 0.94f);
                    }

                    if (body.HasProperty("_MS_Tex"))
                    {
                        body.SetTexture("_MS_Tex", Texture2D.whiteTexture);
                    }

                    joint = new Material(shader);
                    joint.name = "FluidPipes.Coupling";
                    joint.color = new Color(0.82f, 0.84f, 0.86f);
                    machinePanel = new Material(shader)
                    {
                        name = "FluidPipes.MachinePanel",
                        color = new Color(0.27f, 0.31f, 0.34f),
                    };
                    machinePanel.SetFloat("_Metallic", 0.12f);
                    machinePanel.SetFloat("_Glossiness", 0.38f);
                    machineShell = new Material(shader)
                    {
                        name = "FluidPipes.MachineShell",
                        color = new Color(0.80f, 0.83f, 0.85f),
                    };
                    machineShell.SetFloat("_Metallic", 0);
                    machineShell.SetFloat("_Glossiness", 0.46f);
                    // Ambient fill preserves dielectric color within recessed machine sockets.
                    machineShell.EnableKeyword("_EMISSION");
                    machineShell.SetColor("_EmissionColor", new Color(0.20f, 0.22f, 0.24f));
                    if (body.HasProperty("_Metallic"))
                    {
                        body.SetFloat("_Metallic", 0.02f);
                    }

                    if (body.HasProperty("_Glossiness"))
                    {
                        body.SetFloat("_Glossiness", 0.9f);
                    }

                    if (joint.HasProperty("_Metallic"))
                    {
                        joint.SetFloat("_Metallic", 0.98f);
                    }

                    if (joint.HasProperty("_Glossiness"))
                    {
                        joint.SetFloat("_Glossiness", 0.86f);
                    }

                    innerBody = new Material(body)
                    {
                        name = "FluidPipes.GlassInterior",
                        renderQueue = 2999,
                    };
                }
                return body;
            }
        }

        private static Material TankMaterial(bool glass)
        {
            var prefab = LDB.items.Select(2106)?.prefabDesc?.prefab;
            if (prefab == null)
            {
                return null;
            }

            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var material in renderer.sharedMaterials)
                {
                    if (
                        material != null
                        && material.shader.name.Contains(
                            glass ? "PBR Standard Tank" : "PBR Standard Vertex Toggle"
                        )
                    )
                    {
                        return material;
                    }
                }
            }

            return null;
        }

        private static void FreeSteelLighting()
        {
            if (steelEnvironment != null)
            {
                UnityEngine.Object.Destroy(steelEnvironment);
            }

            steelEnvironment = null;
            steelLighting = null;
        }

        internal static MaterialPropertyBlock SteelLighting(Vector3 radial)
        {
            if (steelLighting != null)
            {
                return steelLighting;
            }

            const int size = 64;
            var up = radial.sqrMagnitude > 1 ? radial.normalized : Vector3.up;
            steelEnvironment = new Cubemap(size, TextureFormat.RGBAHalf, true)
            {
                name = "FluidPipes.SteelEnvironment",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
            };
            for (int face = 0; face < 6; face++)
            {
                var pixels = new Color[size * size];
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float a = (x + 0.5f) / size * 2 - 1;
                        float b = (y + 0.5f) / size * 2 - 1;
                        Vector3 direction;
                        switch ((CubemapFace)face)
                        {
                            case CubemapFace.PositiveX:
                                direction = new Vector3(1, -b, -a);
                                break;
                            case CubemapFace.NegativeX:
                                direction = new Vector3(-1, -b, a);
                                break;
                            case CubemapFace.PositiveY:
                                direction = new Vector3(a, 1, b);
                                break;
                            case CubemapFace.NegativeY:
                                direction = new Vector3(a, -1, -b);
                                break;
                            case CubemapFace.PositiveZ:
                                direction = new Vector3(a, -b, 1);
                                break;
                            default:
                                direction = new Vector3(-a, -b, -1);
                                break;
                        }
                        float elevation = Vector3.Dot(direction.normalized, up);
                        float sky = Mathf.SmoothStep(
                            0,
                            1,
                            Mathf.InverseLerp(-0.12f, 0.35f, elevation)
                        );
                        float horizon = Mathf.Exp(-elevation * elevation / 0.018f);
                        pixels[y * size + x] =
                            Color.Lerp(
                                new Color(0.08f, 0.085f, 0.09f),
                                new Color(0.32f, 0.34f, 0.36f),
                                sky
                            )
                            + new Color(0.83f, 0.82f, 0.80f) * horizon;
                        pixels[y * size + x].a = 1;
                    }
                }

                steelEnvironment.SetPixels(pixels, (CubemapFace)face);
            }
            steelEnvironment.Apply(true, true);
            steelLighting = new MaterialPropertyBlock();
            // Pipe draws receive the approximate sky reflection through a material property block.
            steelLighting.SetTexture("unity_SpecCube0", steelEnvironment);
            steelLighting.SetTexture("unity_SpecCube1", steelEnvironment);
            steelLighting.SetVector("unity_SpecCube0_HDR", new Vector4(1, 1, 0, 0));
            steelLighting.SetVector("unity_SpecCube1_HDR", new Vector4(1, 1, 0, 0));
            steelLighting.SetVector("unity_SpecCube0_BoxMin", new Vector4(0, 0, 0, 1));
            steelLighting.SetVector("unity_SpecCube0_ProbePosition", Vector4.zero);
            steelLighting.SetVector("unity_SpecCube1_ProbePosition", Vector4.zero);
            return steelLighting;
        }

        private static void Transparent(Material material, int queue)
        {
            material.SetOverrideTag("RenderType", "Transparent");
            if (material.HasProperty("_Mode"))
            {
                material.SetFloat("_Mode", 3);
                material.SetInt("_SrcBlend", (int)BlendMode.One);
                material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                material.SetInt("_ZWrite", 0);
                material.DisableKeyword("_ALPHATEST_ON");
                material.DisableKeyword("_ALPHABLEND_ON");
                material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            }
            material.renderQueue = queue;
        }

        private static ChunkKey Key(PlanetFactory factory, int entityId)
        {
            var entity = factory.entityPool[entityId];
            Vector3 position = entity.pos;
            var cell = new Vector3Int(
                Mathf.FloorToInt(position.x / ChunkSize),
                Mathf.FloorToInt(position.y / ChunkSize),
                Mathf.FloorToInt(position.z / ChunkSize)
            );
            int group = PipeMaterialLibrary.BatchGroup(factory, entityId);
            return new ChunkKey(cell, entity.protoId, group);
        }

        public static Material LiquidMaterial(int item)
        {
            Color color = FluidAppearance.LinearColor(item);
            color.a = FluidOpacity;
            if (liquids.TryGetValue(item, out var material) && liquidColors[item] == color)
            {
                return material;
            }

            if (material == null)
            {
                var wall = BodyMaterial;
                var fluidShader =
                    Shader.Find("Unlit/Transparent") ?? Shader.Find("Sprites/Default");
                material = fluidShader == null ? new Material(wall) : new Material(fluidShader);
                material.name = "FluidPipes.Liquid." + item;
            }
            if (liquidTints.TryGetValue(item, out var previousTint))
            {
                UnityEngine.Object.Destroy(previousTint);
            }
            var tint = new Texture2D(1, 1, TextureFormat.RGBAHalf, false, true)
            {
                name = "FluidPipes.FluidTint." + item,
            };
            tint.SetPixel(0, 0, color);
            tint.Apply(false, true);
            liquidTints[item] = tint;
            liquidColors[item] = color;
            if (material.HasProperty("_Color"))
            {
                material.color = Color.white;
            }

            material.SetTexture("_MainTex", tint);
            Transparent(material, 2990);
            if (material.HasProperty("_RimColor"))
            {
                var rim = color;
                rim.a = 0.25f;
                material.SetColor("_RimColor", rim);
            }
            if (material.HasProperty("_Metallic"))
            {
                material.SetFloat("_Metallic", 0.02f);
            }

            if (material.HasProperty("_Glossiness"))
            {
                material.SetFloat("_Glossiness", 0.78f);
            }

            if (material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * 0.06f);
            }
            liquids[item] = material;
            return material;
        }

        private static void FreeLiquids(Chunk chunk)
        {
            foreach (var group in chunk.Liquids.Values)
            {
                if (group.Mesh != null)
                {
                    UnityEngine.Object.Destroy(group.Mesh);
                }
            }
            chunk.Liquids.Clear();
        }

        private static Vector3 Origin(ChunkKey key) =>
            new Vector3(key.Cell.x * ChunkSize, key.Cell.y * ChunkSize, key.Cell.z * ChunkSize);

        public static void Clear()
        {
            lock (colliderChangesLock)
            {
                colliderChanges.Clear();
                colliderChangesEpoch++;
            }
            foreach (var chunk in chunks.Values)
            {
                if (chunk.Mesh != null)
                {
                    UnityEngine.Object.Destroy(chunk.Mesh);
                }
                if (chunk.CoreMesh != null)
                {
                    UnityEngine.Object.Destroy(chunk.CoreMesh);
                }

                FreeLiquids(chunk);
            }
            chunks.Clear();
            keys.Clear();
            current = null;
            geometry.Clear();
            FreeSteelLighting();
        }

        public static void Dispose()
        {
            Clear();
            PipeMaterialLibrary.Dispose();
            foreach (var material in new[] { body, joint, innerBody, machinePanel, machineShell })
            {
                if (material != null)
                {
                    UnityEngine.Object.Destroy(material);
                }
            }

            body = joint = innerBody = machinePanel = machineShell = null;
            foreach (var material in liquids.Values)
            {
                UnityEngine.Object.Destroy(material);
            }

            liquids.Clear();
            foreach (var texture in liquidTints.Values)
            {
                UnityEngine.Object.Destroy(texture);
            }

            liquidTints.Clear();
            liquidColors.Clear();
            FreeSteelLighting();
        }

        private static void Mark(int entity, bool refreshCollider = true)
        {
            if (current == null || entity <= 0 || entity >= current.entityPool.Length)
            {
                return;
            }

            if (refreshCollider)
            {
                QueueColliderChange(current, entity);
            }

            if (geometry.TryGetValue(entity, out var cached))
            {
                cached.Dirty = true;
            }

            if (keys.TryGetValue(entity, out var old))
            {
                chunks[old].Dirty = true;
            }

            if (
                !PipeRuntime.IsPipeObject(current, entity)
                || !PipeRuntime.IsPipe(current.cargoTraffic, current.entityPool[entity].beltId)
            )
            {
                geometry.Remove(entity);
                if (keys.TryGetValue(entity, out old))
                {
                    chunks[old].Entities.Remove(entity);
                    keys.Remove(entity);
                }
                return;
            }
            var key = Key(current, entity);
            if (keys.TryGetValue(entity, out old) && !old.Equals(key))
            {
                chunks[old].Entities.Remove(entity);
            }

            if (!chunks.TryGetValue(key, out var chunk))
            {
                chunks.Add(key, chunk = new Chunk());
            }

            chunk.Entities.Add(entity);
            chunk.Dirty = true;
            keys[entity] = key;
        }

        public static void Dirty(PlanetFactory factory, int entity)
        {
            if (factory == null || entity <= 0 || entity >= factory.entityPool.Length)
            {
                return;
            }

            DirtyNode(factory, entity);
            foreach (int other in PipeTopology.Neighbors(factory, entity))
            {
                if (other > 0)
                {
                    DirtyNode(factory, other);
                }
            }

            for (int slot = 0; slot < 4; slot++)
            {
                factory.ReadObjectConn(entity, slot, out _, out int other, out _);
                if (other > 0 && PipeRuntime.IsPipeObject(factory, other))
                {
                    DirtyNode(factory, other);
                }
            }
        }

        private static void DirtyNode(PlanetFactory factory, int entity)
        {
            QueueColliderChange(factory, entity);
            if (factory == current && PipeTypeRegistry.IsMainThread)
            {
                Mark(entity, false);
            }
        }

        private static void QueueColliderChange(PlanetFactory factory, int entity)
        {
            if (factory.planet?.physics?.colChunks == null)
            {
                return;
            }
            lock (colliderChangesLock)
            {
                if (!colliderChanges.TryGetValue(factory, out var changedEntities))
                {
                    colliderChanges.Add(factory, changedEntities = new HashSet<int>());
                }
                changedEntities.Add(entity);
            }
        }

        internal static void FlushColliderChanges()
        {
            if (!PipeTypeRegistry.IsMainThread)
            {
                return;
            }

            Dictionary<PlanetFactory, HashSet<int>> pending;
            int epoch;
            lock (colliderChangesLock)
            {
                if (colliderChanges.Count == 0)
                {
                    return;
                }
                pending = colliderChanges;
                epoch = colliderChangesEpoch;
                colliderChanges = new Dictionary<PlanetFactory, HashSet<int>>();
            }

            foreach (var factoryChanges in pending)
            {
                var factory = factoryChanges.Key;
                var colliderChunks = factory.planet?.physics?.colChunks;
                foreach (int entity in factoryChanges.Value)
                {
                    if (System.Threading.Volatile.Read(ref colliderChangesEpoch) != epoch)
                    {
                        return;
                    }
                    if (factory == current)
                    {
                        Mark(entity, false);
                    }
                    if (colliderChunks != null)
                    {
                        UpdateCollider(factory, entity, colliderChunks);
                    }
                }
            }
        }

        public static void Initialize(PlanetFactory factory)
        {
            if (current == factory)
            {
                return;
            }

            Clear();
            current = factory;
            if (factory?.cargoTraffic == null)
            {
                return;
            }

            for (int index = 1; index < factory.cargoTraffic.beltCursor; index++)
            {
                if (PipeRuntime.IsPipe(factory.cargoTraffic, index))
                {
                    Mark(factory.cargoTraffic.beltPool[index].entityId);
                }
            }
        }

        private static Vector3 Center(PlanetFactory factory, int entityId)
        {
            EntityData entity = factory.entityPool[entityId];
            return entity.pos
                + entity.pos.normalized * PipeTypeRegistry.Geometry(entity.protoId).CenterHeight;
        }

        internal static PipeMesh SelectionGeometry(PlanetFactory factory, int entityId)
        {
            Initialize(factory);
            FlushColliderChanges();
            bool open =
                PipeTypeRegistry
                    .Get(factory.entityPool[entityId].protoId)
                    ?.Definition.Appearance?.WallMode == PipeWallMode.Open;
            return NodeMesh(factory, entityId, open);
        }

        private static NodeGeometry GetNodeGeometry(PlanetFactory factory, int entityId)
        {
            if (!geometry.TryGetValue(entityId, out var node))
            {
                geometry.Add(entityId, node = new NodeGeometry());
            }

            int itemId = factory.entityPool[entityId].protoId;
            if (node.ItemId != itemId)
            {
                node.ItemId = itemId;
                node.Wall = node.Liquid = null;
                node.Dirty = true;
            }

            if (node.Dirty)
            {
                var shape = Shape(factory, entityId, factory.entityPool[entityId].pos);
                if (!shape.Matches(node.Shape))
                {
                    node.Shape = shape;
                    node.Wall = node.Liquid = null;
                }
                node.Dirty = false;
            }
            return node;
        }

        private static PipeMesh NodeMesh(PlanetFactory factory, int entityId, bool liquid)
        {
            var node = GetNodeGeometry(factory, entityId);
            int itemId = node.ItemId;
            var mesh = liquid ? node.Liquid : node.Wall;
            if (mesh != null)
            {
                return mesh;
            }

            var definition = PipeTypeRegistry.Get(itemId)?.Definition;
            var appearance = definition?.Appearance;
            var dimensions = definition?.Geometry ?? PipeGeometryProfile.Default;
            float internalRadius =
                definition?.Behavior == PipeBehavior.Structure
                    ? appearance?.CoreRadiusFraction ?? 0.45f
                    : 0.76f;
            var interior =
                liquid && definition?.Behavior == PipeBehavior.Structure
                    ? appearance?.Interior
                    : null;
            mesh = new PipeMesh(
                liquid && interior == null ? dimensions.Radius * internalRadius : dimensions.Radius,
                !liquid,
                PipeMaterialLibrary.TextureCoordinate(itemId),
                factory.entityPool[entityId].pos,
                dimensions,
                appearance?.TextureCoordinatePeriod ?? 0
            );
            if (!liquid && appearance?.WallMode == PipeWallMode.Open)
            {
                node.Wall = mesh;
                NodeRebuilds++;
                return mesh;
            }
            if (interior == null)
            {
                node.Shape.AddTo(mesh, liquid);
            }
            else
            {
                node.Shape.AddInteriorTo(mesh, interior);
            }
            if (liquid)
            {
                node.Liquid = mesh;
                LiquidRebuilds++;
            }
            else
            {
                node.Wall = mesh;
                NodeRebuilds++;
            }
            return mesh;
        }

        public static void AddEntity(
            PipeMesh builder,
            PlanetFactory factory,
            int entityId,
            Vector3 origin
        ) => Shape(factory, entityId, origin).AddTo(builder);

        public static void UpdateCollider(
            PlanetFactory factory,
            int entityId,
            ColliderContainer[] chunks
        )
        {
            if (entityId <= 0 || !PipeRuntime.IsPipeObject(factory, entityId))
            {
                return;
            }
            if (!PipeTypeRegistry.IsMainThread)
            {
                QueueColliderChange(factory, entityId);
                return;
            }
            int encoded = factory.entityPool[entityId].colliderId;
            if (encoded <= 0 || chunks == null)
            {
                return;
            }

            int chunk = encoded >> 20;

            int index = encoded & 0xFFFFF;
            if (
                chunk >= chunks.Length
                || chunks[chunk] == null
                || index >= chunks[chunk].colliderPool.Length
            )
            {
                return;
            }

            var entity = factory.entityPool[entityId];
            var dimensions = PipeTypeRegistry.Geometry(entity.protoId);
            var inverse = Quaternion.Inverse(entity.rot);
            var shape = CreateShape(factory, entityId, entity.pos, false);
            var bounds = new Bounds(inverse * shape.Center, Vector3.zero);
            foreach (var point in shape.Ends)
            {
                bounds.Encapsulate(inverse * point);
            }

            if (shape.Ends.Count == 0)
            {
                float halfLength = 0.35f * dimensions.Radius / PipeMesh.Radius;
                bounds.Encapsulate(inverse * (shape.Center + shape.Forward * halfLength));
                bounds.Encapsulate(inverse * (shape.Center - shape.Forward * halfLength));
            }
            bounds.Expand(dimensions.Radius * (1 + 0.1f * dimensions.FittingScale) * 2);
            ref var collider = ref chunks[chunk].colliderPool[index];
            Vector3 position = entity.pos + entity.rot * bounds.center;
            if (
                collider.pos.Equals(position)
                && collider.q.Equals(entity.rot)
                && collider.ext.Equals(bounds.extents)
            )
            {
                return;
            }
            collider.pos = position;
            collider.q = entity.rot;
            collider.ext = bounds.extents;
            var physics = factory.planet?.physics;
            if (physics != null && ReferenceEquals(physics.colChunks, chunks))
            {
                physics.SetPlanetPhysicsColliderDirty();
            }
        }

        private static NodeShape Shape(PlanetFactory factory, int entityId2, Vector3 origin)
        {
            return CreateShape(factory, entityId2, origin, true);
        }

        private static NodeShape CreateShape(
            PlanetFactory factory,
            int entityId2,
            Vector3 origin,
            bool resolveCollars
        )
        {
            var entity = factory.entityPool[entityId2];
            bool undirected = PipeTypeRegistry.IsUndirected(entity.protoId);
            bool useTopology = undirected && PipeTopology.Contains(factory, entityId2);
            var dimensions = PipeTypeRegistry.Geometry(entity.protoId);
            var center = Center(factory, entityId2);
            var ends = new List<Vector3>(4);
            var collars = new List<bool>(4);
            var peerRadiusRatios = new List<float>(4);
            var connectedPipes = new List<int>(4);
            int interiorOutputEnd = -1;
            var socketShapes = new List<MachineSocketShape>();
            var socketAxes = new Dictionary<int, Vector3>();
            var socketFrames = new Dictionary<int, Vector3>();
            foreach (
                int other in useTopology
                    ? PipeTopology.Neighbors(factory, entityId2)
                    : Array.Empty<int>()
            )
            {
                if (
                    other <= 0
                    || other >= factory.entityPool.Length
                    || !PipeRuntime.IsPipeObject(factory, other)
                    || !PipeRuntime.IsPipe(factory.cargoTraffic, factory.entityPool[other].beltId)
                    || !PipeTypeRegistry.Compatible(
                        entity.protoId,
                        factory.entityPool[other].protoId
                    )
                )
                {
                    continue;
                }

                var end = (center + Center(factory, other)) * 0.5f;
                if ((end - center).sqrMagnitude < 0.00001f)
                {
                    continue;
                }

                ends.Add(end - origin);
                collars.Add(entityId2 < other);
                connectedPipes.Add(other);
                peerRadiusRatios.Add(
                    PipeTypeRegistry.Geometry(factory.entityPool[other].protoId).Radius
                        / dimensions.Radius
                );
            }
            for (int slot = 0; slot < 4; slot++)
            {
                factory.ReadObjectConn(
                    entityId2,
                    slot,
                    out bool output,
                    out int other,
                    out int otherSlot
                );
                if (
                    other <= 0
                    || other >= factory.entityPool.Length
                    || factory.entityPool[other].id != other
                )
                {
                    continue;
                }

                var peer = factory.entityPool[other];
                bool pipe = PipeRuntime.IsPipeObject(factory, other);
                if (pipe && useTopology)
                {
                    continue;
                }

                Vector3 end;
                if (pipe)
                {
                    if (
                        !PipeConnections.TryReadNativeLink(factory, entityId2, slot, out _, out _)
                        || !PipeRuntime.IsPipe(factory.cargoTraffic, peer.beltId)
                    )
                    {
                        continue;
                    }
                    end = (center + Center(factory, other)) * 0.5f;
                }
                else
                {
                    if (!PipeRuntime.HasInventory(factory, entityId2))
                    {
                        continue;
                    }
                    var machine = PipeMachinePorts.MachineDescription(factory, other);
                    var ports =
                        machine?.slotPoses ?? LDB.items.Select(peer.protoId)?.prefabDesc?.portPoses;
                    if (ports == null || otherSlot < 0 || otherSlot >= ports.Length)
                    {
                        continue;
                    }

                    end =
                        peer.pos
                        + peer.rot * ports[otherSlot].position
                        + (
                            machine == null
                                ? entity.pos.normalized * PipeMesh.CenterHeight
                                : Vector3.zero
                        );
                    if (machine != null)
                    {
                        Vector3 anchor = peer.pos + peer.rot * ports[otherSlot].position;
                        Quaternion orientation = peer.rot * ports[otherSlot].rotation;
                        Vector3 socketUp = orientation * Vector3.up;
                        Vector3 outward = orientation * Vector3.forward;
                        float outletHeight = Vector3.Dot(center - anchor, socketUp);
                        end =
                            anchor + socketUp * outletHeight + outward * PipeMesh.MachinePortDepth;
                        socketShapes.Add(
                            new MachineSocketShape(anchor - origin, orientation, outletHeight)
                        );
                        socketAxes[ends.Count] = -outward;
                        socketFrames[ends.Count] = socketUp;
                    }
                }
                if ((end - center).sqrMagnitude < 0.00001f)
                {
                    continue;
                }

                ends.Add(end - origin);
                connectedPipes.Add(pipe ? other : 0);
                if (pipe && output)
                {
                    interiorOutputEnd = ends.Count - 1;
                }
                peerRadiusRatios.Add(
                    pipe ? PipeTypeRegistry.Geometry(peer.protoId).Radius / dimensions.Radius : 1
                );
                collars.Add(
                    !socketAxes.ContainsKey(ends.Count - 1) && (!pipe || entityId2 < other)
                );
            }
            if (ends.Count == 0 && undirected && !useTopology)
            {
                int belt = entity.beltId;
                if (belt > 0)
                {
                    var segment = factory.cargoTraffic.beltPool[belt];
                    foreach (
                        int peerBelt in new[]
                        {
                            segment.outputId,
                            segment.backInputId,
                            segment.leftInputId,
                            segment.rightInputId,
                        }
                    )
                    {
                        if (PipeRuntime.IsPipe(factory.cargoTraffic, peerBelt))
                        {
                            int peer = factory.cargoTraffic.beltPool[peerBelt].entityId;
                            if (
                                !PipeTypeRegistry.Compatible(
                                    entity.protoId,
                                    factory.entityPool[peer].protoId
                                )
                            )
                            {
                                continue;
                            }
                            ends.Add((center + Center(factory, peer)) * 0.5f - origin);
                            connectedPipes.Add(peer);
                            collars.Add(entityId2 < peer);
                            peerRadiusRatios.Add(
                                PipeTypeRegistry.Geometry(factory.entityPool[peer].protoId).Radius
                                    / dimensions.Radius
                            );
                        }
                    }
                }
            }
            var axes = new List<Vector3>(ends.Count);
            var frames = new List<Vector3>(ends.Count);
            foreach (var end in ends)
            {
                axes.Add(
                    socketAxes.TryGetValue(axes.Count, out var socketAxis)
                        ? socketAxis
                        : (end - (center - origin)).normalized
                );
                frames.Add(
                    socketFrames.TryGetValue(frames.Count, out var socketUp)
                        ? socketUp
                        : (end + origin).normalized
                );
            }
            var shape = new NodeShape
            {
                Position = entity.pos,
                Center = center - origin,
                Up = entity.pos.normalized,
                Forward = entity.rot * Vector3.forward,
                Ends = ends,
                Collars = collars,
                EndPeerRadiusRatios = peerRadiusRatios,
                InteriorOutputEnd = undirected ? -2 : interiorOutputEnd,
                EndAxes = axes,
                EndUps = frames,
            };
            shape.MachineSockets.AddRange(socketShapes);
            if (resolveCollars)
            {
                bool needsFittings = PipeMesh.NeedsJointFittings(shape.Center, ends);
                for (int endIndex = 0; endIndex < ends.Count; endIndex++)
                {
                    int peer = connectedPipes[endIndex];
                    if (peer == 0)
                    {
                        continue;
                    }
                    float peerRadiusRatio = peerRadiusRatios[endIndex];
                    if (peerRadiusRatio < 0.9999f)
                    {
                        collars[endIndex] = true;
                    }
                    else if (peerRadiusRatio > 1f / 0.9999f || !needsFittings)
                    {
                        collars[endIndex] = false;
                    }
                    else
                    {
                        var peerShape = CreateShape(factory, peer, origin, false);
                        collars[endIndex] =
                            !PipeMesh.NeedsJointFittings(peerShape.Center, peerShape.Ends)
                            || entityId2 < peer;
                    }
                }
            }
            return shape;
        }

        private static void DrawSurface(
            PlanetFactory factory,
            Camera camera,
            ChunkKey key,
            Chunk chunk,
            PipeMaterialSet surfaces,
            Mesh mesh,
            Matrix4x4 transform,
            Vector3 radial,
            int submesh,
            PipeSurfaceRole role
        )
        {
            Material surface = surfaces.Get(role);
            if (surface == null)
            {
                return;
            }
            var properties = SteelLighting(radial);
            var definition = PipeTypeRegistry.Get(key.ItemId)?.Definition;
            if (definition?.Appearance?.ApplyEffects != null)
            {
                effectProperties.Clear();
                effectProperties.SetTexture("unity_SpecCube0", steelEnvironment);
                effectProperties.SetTexture("unity_SpecCube1", steelEnvironment);
                effectProperties.SetVector("unity_SpecCube0_HDR", new Vector4(1, 1, 0, 0));
                effectProperties.SetVector("unity_SpecCube1_HDR", new Vector4(1, 1, 0, 0));
                effectProperties.SetVector("unity_SpecCube0_BoxMin", new Vector4(0, 0, 0, 1));
                effectProperties.SetVector("unity_SpecCube0_ProbePosition", Vector4.zero);
                effectProperties.SetVector("unity_SpecCube1_ProbePosition", Vector4.zero);
                if (
                    PipeMaterialLibrary.Effects(
                        key.ItemId,
                        new PipeEffectContext(factory, chunk.Representative, key.Group, role),
                        effectProperties
                    )
                )
                {
                    properties = effectProperties;
                }
            }
            Graphics.DrawMesh(
                mesh,
                transform,
                surface,
                0,
                camera,
                submesh,
                properties,
                role == PipeSurfaceRole.Fittings
                || role == PipeSurfaceRole.Wall && surfaces.WallMode == PipeWallMode.Opaque
                    ? ShadowCastingMode.On
                    : ShadowCastingMode.Off,
                true
            );
        }

        public static int Draw(PlanetFactory factory, Camera camera)
        {
            if (factory?.cargoTraffic == null || camera == null)
            {
                return 0;
            }

            Initialize(factory);
            FlushColliderChanges();
            if (chunks.Count == 0)
            {
                return 0;
            }

            var material = BodyMaterial;
            int drawn = 0;
            var graph = PipeRuntime.Bind(factory.cargoTraffic);
            GeometryUtility.CalculateFrustumPlanes(camera, planes);
            foreach (var entry in chunks)
            {
                var origin = Origin(entry.Key);
                var dimensions = PipeTypeRegistry.Geometry(entry.Key.ItemId);
                float padding = 8 + dimensions.CenterHeight + dimensions.Radius * 2;
                var bounds = new Bounds(
                    origin + Vector3.one * ChunkSize * 0.5f,
                    Vector3.one * (ChunkSize + padding)
                );
                if (!GeometryUtility.TestPlanesAABB(planes, bounds))
                {
                    continue;
                }

                var chunk = entry.Value;
                var type = PipeTypeRegistry.Get(entry.Key.ItemId)?.Definition;
                var surfaces = PipeMaterialLibrary.Get(entry.Key.ItemId);
                material = surfaces.Wall;
                if (chunk.Dirty)
                {
                    int before = NodeRebuilds;
                    foreach (int entityId in chunk.Entities)
                    {
                        NodeMesh(factory, entityId, false);
                    }

                    if (
                        chunk.Mesh == null
                        || chunk.MeshedEntities != chunk.Entities.Count
                        || NodeRebuilds != before
                    )
                    {
                        var builder = new PipeMesh();
                        var coreBuilder = new PipeMesh();
                        chunk.Representative = 0;
                        foreach (int entityId2 in chunk.Entities)
                        {
                            builder.Append(
                                NodeMesh(factory, entityId2, false),
                                factory.entityPool[entityId2].pos - origin
                            );
                            chunk.Representative = entityId2;
                            if (
                                type?.Behavior == PipeBehavior.Structure
                                && type.Appearance?.HasCore == true
                            )
                            {
                                coreBuilder.Append(
                                    NodeMesh(factory, entityId2, true),
                                    factory.entityPool[entityId2].pos - origin
                                );
                            }
                        }

                        if (chunk.Mesh != null)
                        {
                            UnityEngine.Object.Destroy(chunk.Mesh);
                        }

                        chunk.Mesh =
                            builder.VertexCount == 0
                                ? null
                                : builder.Build("FluidPipes.Chunk", true);
                        if (chunk.CoreMesh != null)
                        {
                            UnityEngine.Object.Destroy(chunk.CoreMesh);
                        }

                        chunk.CoreMesh =
                            coreBuilder.VertexCount == 0
                                ? null
                                : coreBuilder.Build("FluidPipes.Conductor");
                        chunk.MeshedEntities = chunk.Entities.Count;
                        Rebuilds++;
                        chunk.LiquidVersion = -1;
                    }
                    chunk.Dirty = false;
                }
                if (chunk.Mesh == null && chunk.CoreMesh == null)
                {
                    continue;
                }

                if (
                    type?.Behavior == PipeBehavior.SharedInventory
                    && chunk.LiquidVersion != graph.TopologyVersion
                )
                {
                    FreeLiquids(chunk);
                    foreach (int entityId3 in chunk.Entities)
                    {
                        var view = graph.View(entityId3);
                        if (!chunk.Liquids.TryGetValue(view.Network, out var group))
                        {
                            chunk.Liquids.Add(
                                view.Network,
                                group = new LiquidGroup { Representative = entityId3 }
                            );
                        }

                        group.Entities.Add(entityId3);
                    }
                    chunk.LiquidVersion = graph.TopologyVersion;
                }
                var transform = Matrix4x4.Translate(origin);
                if (chunk.Mesh != null)
                {
                    if (surfaces.WallMode != PipeWallMode.Opaque)
                    {
                        DrawSurface(
                            factory,
                            camera,
                            entry.Key,
                            chunk,
                            surfaces,
                            chunk.Mesh,
                            transform,
                            bounds.center,
                            2,
                            PipeSurfaceRole.InnerWall
                        );
                    }

                    DrawSurface(
                        factory,
                        camera,
                        entry.Key,
                        chunk,
                        surfaces,
                        chunk.Mesh,
                        transform,
                        bounds.center,
                        0,
                        PipeSurfaceRole.Wall
                    );
                    DrawSurface(
                        factory,
                        camera,
                        entry.Key,
                        chunk,
                        surfaces,
                        chunk.Mesh,
                        transform,
                        bounds.center,
                        1,
                        PipeSurfaceRole.Fittings
                    );
                    if (chunk.Mesh.subMeshCount > 3)
                    {
                        DrawSurface(
                            factory,
                            camera,
                            entry.Key,
                            chunk,
                            surfaces,
                            chunk.Mesh,
                            transform,
                            bounds.center,
                            3,
                            PipeSurfaceRole.MachinePanel
                        );
                    }

                    if (chunk.Mesh.subMeshCount > 4)
                    {
                        DrawSurface(
                            factory,
                            camera,
                            entry.Key,
                            chunk,
                            surfaces,
                            chunk.Mesh,
                            transform,
                            bounds.center,
                            4,
                            PipeSurfaceRole.MachineShell
                        );
                    }
                }

                if (chunk.CoreMesh != null)
                {
                    if (chunk.CoreMesh.GetIndexCount(0) > 0)
                    {
                        DrawSurface(
                            factory,
                            camera,
                            entry.Key,
                            chunk,
                            surfaces,
                            chunk.CoreMesh,
                            transform,
                            bounds.center,
                            0,
                            PipeSurfaceRole.Core
                        );
                    }
                    if (chunk.CoreMesh.GetIndexCount(1) > 0)
                    {
                        DrawSurface(
                            factory,
                            camera,
                            entry.Key,
                            chunk,
                            surfaces,
                            chunk.CoreMesh,
                            transform,
                            bounds.center,
                            1,
                            PipeSurfaceRole.Fittings
                        );
                    }
                }

                drawn++;

                foreach (var group in chunk.Liquids.Values)
                {
                    var view = PipePools.Query(graph, group.Representative);
                    if (view.Count == 0)
                    {
                        continue;
                    }

                    if (group.Mesh == null)
                    {
                        var builder = new PipeMesh();
                        foreach (int entityId4 in group.Entities)
                        {
                            builder.Append(
                                NodeMesh(factory, entityId4, true),
                                factory.entityPool[entityId4].pos - origin
                            );
                        }

                        group.Mesh = builder.Build("FluidPipes.FluidVolume");
                    }
                    Graphics.DrawMesh(
                        group.Mesh,
                        transform,
                        LiquidMaterial(view.Item),
                        0,
                        camera,
                        0,
                        null,
                        ShadowCastingMode.Off,
                        false
                    );
                }
            }
            return drawn;
        }
    }

    [HarmonyPatch]
    internal static class PipeRenderPatches
    {
        [HarmonyPrefix, HarmonyPatch(typeof(CargoTraffic), nameof(CargoTraffic.SetBeltState))]
        public static bool State(CargoTraffic __instance, int beltId)
        {
            // PipeVisuals owns the rendering of procedural pipe meshes.
            return !PipeRuntime.IsPipe(__instance, beltId);
        }

        [HarmonyPrefix, HarmonyPatch(typeof(CargoTraffic), nameof(CargoTraffic.AlterBeltRenderer))]
        public static bool Belt(CargoTraffic __instance, int beltId, ColliderContainer[] colChunks)
        {
            if (!PipeRuntime.IsPipe(__instance, beltId))
            {
                return true;
            }

            ref var belt = ref __instance.beltPool[beltId];
            __instance.RemoveBeltRenderer(beltId);
            PipeVisuals.UpdateCollider(__instance.factory, belt.entityId, colChunks);
            PipeVisuals.Dirty(__instance.factory, belt.entityId);
            return false;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(CargoTraffic), nameof(CargoTraffic.AlterPathRenderer))]
        public static bool Path(CargoTraffic __instance, int pathId)
        {
            var path = __instance.GetCargoPath(pathId);
            return !PipeRuntime.TryPath(path, out _, out _);
        }
    }
}
