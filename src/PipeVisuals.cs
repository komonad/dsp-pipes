using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace FluidPipes
{
    internal static class PipeVisuals
    {
        private const float ChunkSize = 16;

        private sealed class Chunk
        {
            public readonly HashSet<int> Entities = new HashSet<int>();
            public bool Dirty = true;
            public Mesh Mesh;
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

            public bool Matches(NodeShape other)
            {
                if (
                    other == null
                    || !Position.Equals(other.Position)
                    || !Center.Equals(other.Center)
                    || !Up.Equals(other.Up)
                    || Ends.Count != other.Ends.Count
                )
                {
                    return false;
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
                    )
                    {
                        return false;
                    }
                }

                return true;
            }

            public void AddTo(PipeMesh builder)
            {
                builder.Node(Center, Up, Forward, Ends, Collars, EndAxes, EndUps);
            }
        }

        private static PlanetFactory current;
        private static readonly Dictionary<Vector3Int, Chunk> chunks =
            new Dictionary<Vector3Int, Chunk>();
        private static readonly Dictionary<int, Vector3Int> keys =
            new Dictionary<int, Vector3Int>();
        private static readonly Dictionary<int, NodeGeometry> geometry =
            new Dictionary<int, NodeGeometry>();
        private static readonly Plane[] planes = new Plane[6];
        private static Material body;
        private static Material joint;
        private static Material innerBody;
        private static readonly Dictionary<int, Material> liquids = new Dictionary<int, Material>();
        private static readonly List<Texture2D> liquidTints = new List<Texture2D>();
        private static Cubemap steelEnvironment;
        private static MaterialPropertyBlock steelLighting;
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

        private static Vector3Int Key(Vector3 position) =>
            new Vector3Int(
                Mathf.FloorToInt(position.x / ChunkSize),
                Mathf.FloorToInt(position.y / ChunkSize),
                Mathf.FloorToInt(position.z / ChunkSize)
            );

        public static Material LiquidMaterial(int item)
        {
            if (liquids.TryGetValue(item, out var material))
            {
                return material;
            }

            var wall = BodyMaterial;
            var fluidShader = Shader.Find("Unlit/Transparent") ?? Shader.Find("Sprites/Default");
            material = fluidShader == null ? new Material(wall) : new Material(fluidShader);
            material.name = "FluidPipes.Liquid." + item;
            Color color;
            switch (item)
            {
                case 1000:
                    color = new Color(0.25f, 0.7f, 0.75f);
                    break;
                case 1007:
                    color = new Color(0.15f, 0.08f, 0.025f);
                    break;
                case 1114:
                    color = new Color(0.75f, 0.39f, 0.045f);
                    break;
                case 1116:
                    color = new Color(0.92f, 0.8f, 0.12f);
                    break;
                case 1120:
                    color = new Color(0.76f, 0.22f, 0.34f);
                    break;
                case 1121:
                    color = new Color(0.77f, 0.62f, 0.11f);
                    break;
                default:
                    color = Color.HSVToRGB((item * 0.618034f) % 1, 0.55f, 0.65f);
                    break;
            }
            color.a = 0.32f;
            var tint = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                name = "FluidPipes.FluidTint." + item,
            };
            tint.SetPixel(0, 0, color);
            tint.Apply(false, true);
            liquidTints.Add(tint);
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
            liquids.Add(item, material);
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

        private static Vector3 Origin(Vector3Int key) =>
            new Vector3(key.x * ChunkSize, key.y * ChunkSize, key.z * ChunkSize);

        public static void Clear()
        {
            foreach (var chunk in chunks.Values)
            {
                if (chunk.Mesh != null)
                {
                    UnityEngine.Object.Destroy(chunk.Mesh);
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
            foreach (var material in new[] { body, joint, innerBody })
            {
                if (material != null)
                {
                    UnityEngine.Object.Destroy(material);
                }
            }

            body = joint = innerBody = null;
            foreach (var material in liquids.Values)
            {
                UnityEngine.Object.Destroy(material);
            }

            liquids.Clear();
            foreach (var texture in liquidTints)
            {
                UnityEngine.Object.Destroy(texture);
            }

            liquidTints.Clear();
            FreeSteelLighting();
        }

        private static void Mark(int entity)
        {
            if (current == null || entity <= 0 || entity >= current.entityPool.Length)
            {
                return;
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
            var key = Key(current.entityPool[entity].pos);
            if (keys.TryGetValue(entity, out old) && old != key)
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
            if (factory != current || entity <= 0)
            {
                return;
            }

            Mark(entity);
            foreach (int other in PipeTopology.Neighbors(factory, entity))
            {
                if (other > 0)
                {
                    Mark(other);
                }
            }

            for (int slot = 0; slot < 4; slot++)
            {
                factory.ReadObjectConn(entity, slot, out _, out int other, out _);
                if (other > 0 && PipeRuntime.IsPipeObject(factory, other))
                {
                    Mark(other);
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

        private static Vector3 Center(Vector3 point) =>
            point + point.normalized * (PipeMesh.Radius + 0.06f);

        private static PipeMesh NodeMesh(PlanetFactory factory, int entityId, bool liquid)
        {
            if (!geometry.TryGetValue(entityId, out var node))
            {
                geometry.Add(entityId, node = new NodeGeometry());
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
            var mesh = liquid ? node.Liquid : node.Wall;
            if (mesh != null)
            {
                return mesh;
            }

            mesh = new PipeMesh(liquid ? PipeMesh.Radius * 0.76f : PipeMesh.Radius, !liquid);
            node.Shape.AddTo(mesh);
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
            var inverse = Quaternion.Inverse(entity.rot);
            var shape = Shape(factory, entityId, entity.pos);
            var bounds = new Bounds(inverse * shape.Center, Vector3.zero);
            foreach (var point in shape.Ends)
            {
                bounds.Encapsulate(inverse * point);
            }

            if (shape.Ends.Count == 0)
            {
                bounds.Encapsulate(inverse * (shape.Center + shape.Forward * 0.35f));
                bounds.Encapsulate(inverse * (shape.Center - shape.Forward * 0.35f));
            }
            bounds.Expand(PipeMesh.Radius * 2);
            ref var collider = ref chunks[chunk].colliderPool[index];
            collider.pos = entity.pos + entity.rot * bounds.center;
            collider.q = entity.rot;
            collider.ext = bounds.extents;
        }

        private static NodeShape Shape(PlanetFactory factory, int entityId2, Vector3 origin)
        {
            var entity = factory.entityPool[entityId2];
            var center = Center(entity.pos);
            var ends = new List<Vector3>(4);
            var collars = new List<bool>(4);
            foreach (int other in PipeTopology.Neighbors(factory, entityId2))
            {
                if (
                    other <= 0
                    || other >= factory.entityPool.Length
                    || !PipeRuntime.IsPipeObject(factory, other)
                )
                {
                    continue;
                }

                var end = (center + Center(factory.entityPool[other].pos)) * 0.5f;
                if ((end - center).sqrMagnitude < 0.00001f)
                {
                    continue;
                }

                ends.Add(end - origin);
                collars.Add(entityId2 < other);
            }
            for (int slot = 0; slot < 4; slot++)
            {
                factory.ReadObjectConn(entityId2, slot, out _, out int other, out int otherSlot);
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
                if (pipe && PipeTopology.Contains(factory, entityId2))
                {
                    continue;
                }

                Vector3 end;
                if (pipe)
                {
                    end = (center + Center(peer.pos)) * 0.5f;
                }
                else
                {
                    var ports = LDB.items.Select(peer.protoId)?.prefabDesc?.portPoses;
                    if (ports == null || otherSlot < 0 || otherSlot >= ports.Length)
                    {
                        continue;
                    }

                    end =
                        peer.pos
                        + peer.rot * ports[otherSlot].position
                        + entity.pos.normalized * (PipeMesh.Radius + 0.06f);
                }
                if ((end - center).sqrMagnitude < 0.00001f)
                {
                    continue;
                }

                ends.Add(end - origin);
                collars.Add(!pipe || entityId2 < other);
            }
            if (ends.Count == 0 && !PipeTopology.Contains(factory, entityId2))
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
                            ends.Add(
                                (center + Center(factory.entityPool[peer].pos)) * 0.5f - origin
                            );
                            collars.Add(entityId2 < peer);
                        }
                    }
                }
            }
            var axes = new List<Vector3>(ends.Count);
            var frames = new List<Vector3>(ends.Count);
            foreach (var end in ends)
            {
                axes.Add((end - (center - origin)).normalized);
                frames.Add((end + origin).normalized);
            }
            return new NodeShape
            {
                Position = entity.pos,
                Center = center - origin,
                Up = entity.pos.normalized,
                Forward = entity.rot * Vector3.forward,
                Ends = ends,
                Collars = collars,
                EndAxes = axes,
                EndUps = frames,
            };
        }

        public static int Draw(PlanetFactory factory, Camera camera)
        {
            if (factory?.cargoTraffic == null || camera == null)
            {
                return 0;
            }

            Initialize(factory);
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
                var bounds = new Bounds(
                    origin + Vector3.one * ChunkSize * 0.5f,
                    Vector3.one * (ChunkSize + 8)
                );
                if (!GeometryUtility.TestPlanesAABB(planes, bounds))
                {
                    continue;
                }

                var chunk = entry.Value;
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
                        foreach (int entityId2 in chunk.Entities)
                        {
                            builder.Append(
                                NodeMesh(factory, entityId2, false),
                                factory.entityPool[entityId2].pos - origin
                            );
                        }

                        if (chunk.Mesh != null)
                        {
                            UnityEngine.Object.Destroy(chunk.Mesh);
                        }

                        chunk.Mesh =
                            builder.VertexCount == 0
                                ? null
                                : builder.Build("FluidPipes.Chunk", true);
                        chunk.MeshedEntities = chunk.Entities.Count;
                        Rebuilds++;
                        chunk.LiquidVersion = -1;
                    }
                    chunk.Dirty = false;
                }
                if (chunk.Mesh == null)
                {
                    continue;
                }

                if (chunk.LiquidVersion != graph.TopologyVersion)
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
                // Draw every far-facing inner wall before near-facing outer walls, independently of node and chunk order.
                Graphics.DrawMesh(
                    chunk.Mesh,
                    transform,
                    innerBody,
                    0,
                    camera,
                    2,
                    null,
                    ShadowCastingMode.Off,
                    false
                );
                Graphics.DrawMesh(
                    chunk.Mesh,
                    transform,
                    material,
                    0,
                    camera,
                    0,
                    null,
                    ShadowCastingMode.Off,
                    false
                );
                Graphics.DrawMesh(
                    chunk.Mesh,
                    transform,
                    joint,
                    0,
                    camera,
                    1,
                    SteelLighting(bounds.center),
                    ShadowCastingMode.On,
                    true
                );
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
                        var builder = new PipeMesh(PipeMesh.Radius * 0.76f, false);
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
