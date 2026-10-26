using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace FluidPipes.Integration.Belts
{
    [HarmonyPatch]
    internal static class PipePreviewRotationPatches
    {
        [
            HarmonyPostfix,
            HarmonyPriority(Priority.Last),
            HarmonyPatch(typeof(BuildTool_Path), nameof(BuildTool_Path.DeterminePreviews))
        ]
        public static void InitializePlannedRotations(BuildTool_Path __instance)
        {
            if (!PipeTypeRegistry.IsItem(__instance.handItem?.ID))
            {
                return;
            }

            var previews = __instance.buildPreviews;
            for (int index = 0; index < previews.Count; index++)
            {
                var preview = previews[index];
                if (HasPlannedRotation(preview.lrot))
                {
                    continue;
                }

                Vector3 center = Center(preview);
                Vector3 before = index > 0 ? Center(previews[index - 1]) : center;
                Vector3 after = index + 1 < previews.Count ? Center(previews[index + 1]) : center;
                Vector3 tangent = after - before;
                if (tangent.sqrMagnitude < 0.000001f)
                {
                    tangent =
                        preview.output == null ? Vector3.zero : Center(preview.output) - center;
                }
                preview.lrot = preview.lrot2 = PlannedRotation(preview.lpos, tangent);
            }
        }

        private static Vector3 Center(BuildPreview preview) =>
            preview.lpos
            + preview.lpos.normalized * PipeTypeRegistry.Geometry(preview.item?.ID).CenterHeight;

        private static Quaternion PlannedRotation(Vector3 position, Vector3 tangent)
        {
            Quaternion surfaceRotation = Maths.SphericalRotation(position, 0);
            if (tangent.sqrMagnitude < 0.000001f)
            {
                return surfaceRotation;
            }

            tangent.Normalize();
            Vector3 up = position.normalized;
            if (Mathf.Abs(Vector3.Dot(tangent, up)) > 0.9999f)
            {
                up = surfaceRotation * Vector3.forward;
            }
            return Quaternion.LookRotation(tangent, up);
        }

        private static bool HasPlannedRotation(Quaternion rotation)
        {
            float lengthSquared = Quaternion.Dot(rotation, rotation);
            return !float.IsNaN(lengthSquared)
                && !float.IsInfinity(lengthSquared)
                && Mathf.Abs(lengthSquared - 1) < 0.001f
                && rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z
                    > 0.00000001f;
        }

        [
            HarmonyTranspiler,
            HarmonyPatch(typeof(BuildTool_Path), nameof(BuildTool_Path.CreatePrebuilds))
        ]
        public static IEnumerable<CodeInstruction> PreservePreviewRotation(
            IEnumerable<CodeInstruction> instructions
        )
        {
            var code = new List<CodeInstruction>(instructions);
            var sphericalRotation = AccessTools.Method(
                typeof(Maths),
                nameof(Maths.SphericalRotation),
                new[] { typeof(Vector3), typeof(float) }
            );
            var position = AccessTools.Field(typeof(BuildPreview), nameof(BuildPreview.lpos));
            var selectRotation = AccessTools.Method(
                typeof(PipePreviewRotationPatches),
                nameof(SelectRotation)
            );
            int replacements = 0;
            for (int index = 0; index < code.Count; index++)
            {
                CodeInstruction instruction = code[index];
                if (!instruction.Calls(sphericalRotation))
                {
                    yield return instruction;
                    continue;
                }
                if (index < 3 || !code[index - 2].LoadsField(position))
                {
                    throw new InvalidOperationException(
                        "The native prebuild rotation call requires its BuildPreview position."
                    );
                }

                CodeInstruction preview = code[index - 3];
                var loadPreview = new CodeInstruction(preview.opcode, preview.operand);
                loadPreview.labels.AddRange(instruction.labels);
                loadPreview.blocks.AddRange(instruction.blocks);
                yield return loadPreview;
                yield return new CodeInstruction(OpCodes.Call, selectRotation);
                replacements++;
            }
            if (replacements != 2)
            {
                throw new InvalidOperationException(
                    "The native path builder requires two preview/prebuild rotation sites."
                );
            }
        }

        private static Quaternion SelectRotation(Vector3 position, float yaw, BuildPreview preview)
        {
            // 原生路径预览以单位变换开始；初始化姿态保留规划切线。
            return PipeTypeRegistry.IsItem(preview.item?.ID) && HasPlannedRotation(preview.lrot)
                ? preview.lrot
                : Maths.SphericalRotation(position, yaw);
        }

        [
            HarmonyTranspiler,
            HarmonyPatch(
                typeof(BuildTool_Dismantle),
                nameof(BuildTool_Dismantle.UpdatePreviewModels)
            )
        ]
        public static IEnumerable<CodeInstruction> DismantleSigns(
            IEnumerable<CodeInstruction> instructions
        )
        {
            var code = new List<CodeInstruction>(instructions);
            var addSign = AccessTools.Method(
                typeof(ConnGizmoRenderer),
                nameof(ConnGizmoRenderer.AddXSign)
            );
            var rotation = AccessTools.Field(typeof(BuildPreview), nameof(BuildPreview.lrot));
            var addPipeSign = AccessTools.Method(
                typeof(PipePreviewRotationPatches),
                nameof(AddDismantleSign)
            );
            int replacements = 0;
            for (int index = 0; index < code.Count; index++)
            {
                CodeInstruction instruction = code[index];
                if (!instruction.Calls(addSign))
                {
                    yield return instruction;
                    continue;
                }
                if (index < 2 || !code[index - 1].LoadsField(rotation))
                {
                    throw new InvalidOperationException(
                        "The native dismantle marker requires its BuildPreview rotation."
                    );
                }

                CodeInstruction preview = code[index - 2];
                var loadPreview = new CodeInstruction(preview.opcode, preview.operand);
                loadPreview.labels.AddRange(instruction.labels);
                loadPreview.blocks.AddRange(instruction.blocks);
                yield return loadPreview;
                yield return new CodeInstruction(OpCodes.Call, addPipeSign);
                replacements++;
            }
            if (replacements != 1)
            {
                throw new InvalidOperationException(
                    "The native dismantle tool requires one marker site."
                );
            }
        }

        private static void AddDismantleSign(
            ConnGizmoRenderer renderer,
            Vector3 position,
            Quaternion rotation,
            BuildPreview preview
        )
        {
            if (PipeTypeRegistry.IsItem(preview.item?.ID))
            {
                rotation = Maths.SphericalRotation(position, 0);
            }
            renderer.AddXSign(position, rotation);
        }
    }
}
