using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace FluidPipes
{
    [HarmonyPatch]
    internal static class PipePlanningGizmos
    {
        private sealed class Lines
        {
            public bool PipeCursor;
            public readonly List<ConnGizmoObj> Items = new List<ConnGizmoObj>();
            public ComputeBuffer Buffer;
            public ComputeBuffer Arguments;
            public readonly uint[] Counts = new uint[5];
            public readonly MaterialPropertyBlock Properties = new MaterialPropertyBlock();

            public void Dispose()
            {
                Buffer?.Release();
                Arguments?.Release();
            }
        }

        private static readonly ConditionalWeakTable<ConnGizmoRenderer, Lines> states =
            new ConditionalWeakTable<ConnGizmoRenderer, Lines>();

        [ThreadStatic]
        private static bool pipePrebuild;

        [ThreadStatic]
        private static bool registeredPipePrebuild;
        private static Mesh mesh;
        private static Material material;

        private static Lines State(ConnGizmoRenderer renderer) =>
            states.GetValue(renderer, _ => new Lines());

        [
            HarmonyPostfix,
            HarmonyPatch(
                typeof(BuildTool_Path),
                nameof(BuildTool_Path.UpdatePreviewModelConditions)
            )
        ]
        public static void Preview(BuildTool_Path __instance, BuildModel model)
        {
            if (model.connRenderer != null)
            {
                State(model.connRenderer).PipeCursor = PipeTypeRegistry.IsUndirected(
                    __instance.handItem?.ID
                );
            }
        }

        [HarmonyPrefix, HarmonyPatch(typeof(ConnGizmoRenderer), nameof(ConnGizmoRenderer.ClearAll))]
        public static void Clear(ConnGizmoRenderer __instance)
        {
            if (states.TryGetValue(__instance, out var state))
            {
                state.PipeCursor = false;
                state.Items.Clear();
            }
        }

        [HarmonyPrefix, HarmonyPatch(typeof(ConnGizmoRenderer), nameof(ConnGizmoRenderer.Update))]
        public static void Begin(ConnGizmoRenderer __instance)
        {
            if (
                __instance.factory == null
                || __instance.factoryModel == null
                || __instance.factoryModel.needRefreshConnGizmo012
                || __instance.cursorGizmoGraph.pointCount > 0
            )
            {
                State(__instance).Items.Clear();
            }

            pipePrebuild = false;
            registeredPipePrebuild = false;
        }

        [
            HarmonyTranspiler,
            HarmonyPatch(typeof(ConnGizmoRenderer), nameof(ConnGizmoRenderer.Update))
        ]
        public static IEnumerable<CodeInstruction> Update(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            var add = AccessTools.Method(
                typeof(List<ConnGizmoObj>),
                nameof(List<ConnGizmoObj>.Add)
            );
            var lines = AccessTools.Field(typeof(ConnGizmoRenderer), "objs_2");
            var endpoints = AccessTools.Field(typeof(ConnGizmoRenderer), "objs_0");
            var intermediatePoints = AccessTools.Field(typeof(ConnGizmoRenderer), "objs_1");
            int last = -1;
            bool isLine = false;
            for (int index = 0; index < code.Count; index++)
            {
                if (
                    code[index].opcode == OpCodes.Ldfld
                    && code[index].operand is FieldInfo field
                    && field.FieldType == typeof(List<ConnGizmoObj>)
                )
                {
                    isLine = Equals(field, lines);
                }

                if (code[index].Calls(add))
                {
                    if (isLine)
                    {
                        last = index;
                    }
                    isLine = false;
                }
            }
            isLine = false;
            bool isPoint = false;
            for (int index2 = 0; index2 < code.Count; index2++)
            {
                var instruction = code[index2];
                if (
                    instruction.opcode == OpCodes.Ldfld
                    && instruction.operand is FieldInfo field
                    && field.FieldType == typeof(List<ConnGizmoObj>)
                )
                {
                    isLine = Equals(field, lines);
                    isPoint = Equals(field, endpoints) || Equals(field, intermediatePoints);
                }

                if (
                    instruction.operand is MethodInfo method
                    && method.Name == "Select"
                    && method.ReturnType == typeof(ItemProto)
                )
                {
                    var replacement = new CodeInstruction(
                        OpCodes.Call,
                        AccessTools.Method(typeof(PipePlanningGizmos), nameof(Select))
                    );
                    replacement.labels.AddRange(instruction.labels);
                    replacement.blocks.AddRange(instruction.blocks);
                    yield return replacement;
                }
                else if (isPoint && instruction.Calls(add))
                {
                    var replacement = new CodeInstruction(
                        OpCodes.Call,
                        AccessTools.Method(typeof(PipePlanningGizmos), nameof(AddPoint))
                    );
                    replacement.labels.AddRange(instruction.labels);
                    replacement.blocks.AddRange(instruction.blocks);
                    yield return replacement;
                    isPoint = false;
                }
                else if (isLine && instruction.Calls(add))
                {
                    var load = new CodeInstruction(OpCodes.Ldarg_0);
                    load.labels.AddRange(instruction.labels);
                    load.blocks.AddRange(instruction.blocks);
                    yield return load;
                    yield return new CodeInstruction(
                        OpCodes.Call,
                        AccessTools.Method(
                            typeof(PipePlanningGizmos),
                            index2 == last ? nameof(AddCursor) : nameof(AddPrebuild)
                        )
                    );
                    isLine = false;
                }
                else
                {
                    yield return instruction;
                }
            }
        }

        public static ItemProto Select(ProtoSet<ItemProto> set, int itemId)
        {
            pipePrebuild = PipeTypeRegistry.IsUndirected(itemId);
            registeredPipePrebuild = PipeTypeRegistry.IsItem(itemId);
            return set.Select(itemId);
        }

        public static void AddPoint(List<ConnGizmoObj> native, ConnGizmoObj item)
        {
            if (registeredPipePrebuild)
            {
                item.rot = Maths.SphericalRotation(item.pos, 0);
            }
            native.Add(item);
        }

        public static void AddPrebuild(
            List<ConnGizmoObj> native,
            ConnGizmoObj item,
            ConnGizmoRenderer renderer
        )
        {
            if (pipePrebuild)
            {
                State(renderer).Items.Add(item);
            }
            else
            {
                native.Add(item);
            }
        }

        public static void AddCursor(
            List<ConnGizmoObj> native,
            ConnGizmoObj item,
            ConnGizmoRenderer renderer
        )
        {
            var state = State(renderer);
            if (state.PipeCursor)
            {
                state.Items.Add(item);
            }
            else
            {
                native.Add(item);
            }
        }

        public static Mesh LineMesh
        {
            get
            {
                if (mesh != null)
                {
                    return mesh;
                }

                mesh = UnityEngine.Object.Instantiate(Configs.builtin.solidGizmoMeshes[2]);
                mesh.name = "FluidPipes.UndirectedLine";
                var vertices = mesh.vertices;
                var indices = mesh.triangles;
                var kept = new List<int>();
                for (int index = 0; index < indices.Length; index += 3)
                {
                    if (
                        Body(vertices[indices[index]])
                        && Body(vertices[indices[index + 1]])
                        && Body(vertices[indices[index + 2]])
                    )
                    {
                        kept.Add(indices[index]);
                        kept.Add(indices[index + 1]);
                        kept.Add(indices[index + 2]);
                    }
                }

                if (kept.Count == 0)
                {
                    throw new InvalidOperationException(
                        "Connection line mesh has no rectangular body."
                    );
                }

                mesh.triangles = kept.ToArray();
                return mesh;
            }
        }

        private static bool Body(Vector3 position) =>
            Mathf.Abs(position.x) <= 0.061f
            && position.y <= 0.121f
            && position.z >= -0.001f
            && position.z <= 1.001f;

        [HarmonyPostfix, HarmonyPatch(typeof(ConnGizmoRenderer), nameof(ConnGizmoRenderer.Draw))]
        public static void Draw(ConnGizmoRenderer __instance)
        {
            if (!states.TryGetValue(__instance, out var state) || state.Items.Count == 0)
            {
                return;
            }

            if (material == null)
            {
                material = new Material(Configs.builtin.solidGizmoMats[2])
                {
                    name = "FluidPipes.UndirectedLine",
                };
            }

            if (state.Buffer == null || state.Buffer.count < state.Items.Count)
            {
                state.Buffer?.Release();
                state.Buffer = new ComputeBuffer(
                    Mathf.NextPowerOfTwo(Mathf.Max(64, state.Items.Count)),
                    36
                );
            }
            if (state.Arguments == null)
            {
                state.Arguments = new ComputeBuffer(5, 4, ComputeBufferType.IndirectArguments);
            }

            state.Buffer.SetData(state.Items, 0, 0, state.Items.Count);
            state.Counts[0] = LineMesh.GetIndexCount(0);
            state.Counts[1] = (uint)state.Items.Count;
            state.Arguments.SetData(state.Counts);
            state.Properties.SetBuffer("_ObjBuffer", state.Buffer);
            Graphics.DrawMeshInstancedIndirect(
                mesh,
                0,
                material,
                new Bounds(Vector3.zero, Vector3.one * 10000),
                state.Arguments,
                0,
                state.Properties,
                ShadowCastingMode.Off,
                false
            );
        }

        [HarmonyPostfix, HarmonyPatch(typeof(ConnGizmoRenderer), nameof(ConnGizmoRenderer.Destroy))]
        public static void Destroy(ConnGizmoRenderer __instance)
        {
            if (states.TryGetValue(__instance, out var state))
            {
                state.Dispose();
                states.Remove(__instance);
            }
        }
    }
}
