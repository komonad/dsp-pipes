using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace FluidPipes
{
    [HarmonyPatch]
    internal static class PipeSelectionHighlights
    {
        private sealed class NodeOutline
        {
            internal PipeMesh Geometry;
            internal Mesh Mesh;
            internal Vector3 Position;
            internal int Revision;
        }

        private static readonly Dictionary<int, NodeOutline> nodes =
            new Dictionary<int, NodeOutline>();
        private static readonly List<int> expiredNodes = new List<int>();
        private static PlanetFactory factory;
        private static Material mask;
        private static Material adjacentComposite;
        private static Material adjacentUpsample;
        private static OutlineRenderer outlineRenderer;
        private static CommandBuffer adjacentCommands;
        private static int selectedEntity;
        private static int revision;

        internal static int Count => nodes.Count;
        internal static int MeshRebuilds { get; private set; }
        internal static int SelectedEntity => selectedEntity;
        internal static int SelectedDraws { get; private set; }
        internal static int AdjacentDraws { get; private set; }
        private static bool Active =>
            factory != null
            && factory == GameMain.localPlanet?.factory
            && nodes.Count > 0
            && UIGame.viewMode != EViewMode.Globe;

        internal static void Show(PlanetFactory owner, int entityId, int[] peers)
        {
            if (factory != owner)
            {
                Hide();
                factory = owner;
            }
            selectedEntity = entityId;
            revision++;
            Include(entityId);
            foreach (int peer in peers)
            {
                Include(peer);
            }
            expiredNodes.Clear();
            foreach (var entry in nodes)
            {
                if (entry.Value.Revision != revision)
                {
                    expiredNodes.Add(entry.Key);
                }
            }
            foreach (int expired in expiredNodes)
            {
                Object.Destroy(nodes[expired].Mesh);
                nodes.Remove(expired);
            }
        }

        private static void Include(int entityId)
        {
            if (entityId <= 0 || !PipeRuntime.IsPipeObject(factory, entityId))
            {
                return;
            }
            var geometry = PipeVisuals.SelectionGeometry(factory, entityId);
            if (!nodes.TryGetValue(entityId, out var node))
            {
                node = new NodeOutline();
                nodes.Add(entityId, node);
            }
            if (!ReferenceEquals(node.Geometry, geometry))
            {
                if (node.Mesh != null)
                {
                    Object.Destroy(node.Mesh);
                }
                node.Geometry = geometry;
                node.Mesh = geometry.Build("FluidPipes.NodeOutline", true);
                MeshRebuilds++;
            }
            node.Position = factory.entityPool[entityId].pos;
            node.Revision = revision;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(OutlineRenderer), "OnPostRender")]
        private static void EnableOutlinePass()
        {
            if (Active && Camera.current == GameCamera.main)
            {
                OutlineRenderer.draw = true;
            }
        }

        [
            HarmonyPostfix,
            HarmonyPatch(typeof(PlayerControlGizmo), nameof(PlayerControlGizmo.OnOutlineDraw))
        ]
        private static void DrawOutlines(CommandBuffer commandBuffer)
        {
            if (!Active || Camera.current != GameCamera.main)
            {
                return;
            }
            if (mask == null)
            {
                mask = new Material(Shader.Find("Unlit/Color"))
                {
                    name = "FluidPipes.OutlineMask",
                    color = Color.white,
                };
            }
            if (nodes.TryGetValue(selectedEntity, out var selected))
            {
                DrawNode(commandBuffer, selected);
                SelectedDraws = 1;
            }
            OutlineRenderer.colorIndex = 3;
            OutlineRenderer.sundot = 1;
        }

        private static void DrawNode(CommandBuffer commands, NodeOutline node)
        {
            Matrix4x4 transform = Matrix4x4.Translate(node.Position);
            for (int submesh = 0; submesh < node.Mesh.subMeshCount; submesh++)
            {
                // 外壁与不透明接头构成节点轮廓。
                if (submesh != 2)
                {
                    commands.DrawMesh(node.Mesh, transform, mask, submesh);
                }
            }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(OutlineRenderer), "OnPostRender")]
        private static void DrawAdjacentOutlines(OutlineRenderer __instance)
        {
            if (
                !Active
                || Camera.current != GameCamera.main
                || nodes.Count <= 1
                || __instance.rtex == null
                || __instance.rtex.Length < 2
            )
            {
                return;
            }
            AdjacentDraws = 0;
            if (outlineRenderer != __instance)
            {
                FreeCompositeMaterials();
                outlineRenderer = __instance;
                adjacentComposite = new Material(__instance.excludeMat)
                {
                    name = "FluidPipes.AdjacentOutlineComposite",
                };
                adjacentUpsample = new Material(__instance.upSampleMat)
                {
                    name = "FluidPipes.AdjacentOutlineUpsample",
                };
                adjacentUpsample.SetFloat("_PreserveVeinSunData", 0);
                adjacentCommands = new CommandBuffer { name = "FluidPipes Adjacent Pipe Outlines" };
            }
            var target = RenderTexture.active;
            try
            {
                var buffers = __instance.rtex;
                adjacentCommands.Clear();
                adjacentCommands.SetRenderTarget(buffers[0]);
                adjacentCommands.ClearRenderTarget(true, true, Color.clear);
                foreach (var entry in nodes)
                {
                    if (entry.Key != selectedEntity)
                    {
                        DrawNode(adjacentCommands, entry.Value);
                        AdjacentDraws++;
                    }
                }
                Graphics.ExecuteCommandBuffer(adjacentCommands);
                for (int index = 0; index + 1 < __instance.bufferCount; index++)
                {
                    OutlineRenderer.Blit(
                        buffers[index],
                        buffers[index + 1],
                        __instance.downSampleMat,
                        0
                    );
                }
                for (int index = __instance.bufferCount - 1; index >= 2; index--)
                {
                    OutlineRenderer.Blit(
                        buffers[index],
                        buffers[1],
                        adjacentUpsample,
                        0,
                        false,
                        false,
                        __instance.alphas[index]
                    );
                }
                adjacentComposite.SetTexture("_ExcludeTex", buffers[0]);
                adjacentComposite.SetColor("_TintColor", new Color(0.2f, 0.8f, 1, 1));
                adjacentComposite.SetFloat("_SunDot", 1);
                adjacentComposite.SetFloat("_UseVeinOutlineStyle", 0);
                OutlineRenderer.Blit(buffers[1], target, adjacentComposite, 0, false);
            }
            finally
            {
                RenderTexture.active = target;
            }
        }

        private static void FreeCompositeMaterials()
        {
            if (adjacentComposite != null)
            {
                Object.Destroy(adjacentComposite);
                Object.Destroy(adjacentUpsample);
            }
            adjacentComposite = adjacentUpsample = null;
            adjacentCommands?.Release();
            adjacentCommands = null;
            outlineRenderer = null;
        }

        internal static void Hide()
        {
            foreach (var node in nodes.Values)
            {
                Object.Destroy(node.Mesh);
            }
            nodes.Clear();
            expiredNodes.Clear();
            factory = null;
            selectedEntity = 0;
            SelectedDraws = AdjacentDraws = 0;
        }

        internal static void Dispose()
        {
            Hide();
            FreeCompositeMaterials();
            if (mask != null)
            {
                Object.Destroy(mask);
                mask = null;
            }
        }
    }
}
