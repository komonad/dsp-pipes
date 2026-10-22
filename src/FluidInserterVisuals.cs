using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace FluidPipes
{
    internal static class FluidInserterVisuals
    {
        private sealed class Shape
        {
            public Mesh Mesh;
            public Vector3 Origin;
            public Vector3 Start;
            public Vector3 End;
            public Vector3 Up;
            public FluidInserterMesh.Socket Source;
            public FluidInserterMesh.Socket Destination;
            public int Grade;
            public bool HasShape;
            public bool Dirty;
            public bool Error;

            public void Set(FluidInserterMesh.Socket source, FluidInserterMesh.Socket destination)
            {
                var start = source.Position;
                var end = destination.Position;
                if (HasShape && Source.Equals(source) && Destination.Equals(destination))
                {
                    return;
                }

                Start = start;
                End = end;
                Origin = start;
                Up = (start + end).normalized;
                Source = source;
                Destination = destination;
                HasShape = true;
                Dirty = true;
            }

            public void EnsureMesh()
            {
                if (!Dirty)
                {
                    return;
                }
                if (Mesh != null)
                {
                    UnityEngine.Object.Destroy(Mesh);
                }
                Mesh = FluidInserterMesh.Create(
                    Source.Local(Origin),
                    Destination.Local(Origin),
                    Up
                );
                Dirty = false;
                Rebuilds++;
            }

            public void Free()
            {
                if (Mesh != null)
                {
                    UnityEngine.Object.Destroy(Mesh);
                }
                Mesh = null;
                HasShape = false;
            }
        }

        private static PlanetFactory current;
        private static readonly Dictionary<int, Shape> shapes = new Dictionary<int, Shape>();
        private static readonly List<Shape> previews = new List<Shape>();
        private static readonly Plane[] planes = new Plane[6];
        private static readonly FieldInfo instBuffer = AccessTools.Field(
            typeof(ObjectRenderer),
            "instBuffer"
        );
        private static readonly FieldInfo poseBuffer = AccessTools.Field(
            typeof(InserterRenderer),
            "pose2Buffer"
        );
        private static Material dark;
        private static Material preview;
        private static readonly Material[] accents = new Material[5];
        private static readonly MaterialPropertyBlock previewProperties =
            new MaterialPropertyBlock();
        private static int previewFrame = -1;
        private static int previewCount;
        public static int Rebuilds { get; private set; }
        public static int Count => shapes.Count;

        public static bool UsesPipe(PlanetFactory factory, int obj)
        {
            if (factory == null || obj == 0)
            {
                return false;
            }

            if (obj > 0)
            {
                if (obj >= factory.entityPool.Length || factory.entityPool[obj].id != obj)
                {
                    return false;
                }

                int entityId = factory.entityPool[obj].inserterId;
                var system = factory.factorySystem;
                return entityId > 0
                    && entityId < system.inserterPool.Length
                    && system.inserterPool[entityId].id == entityId
                    && (
                        PipeRuntime.IsPipeObject(factory, system.inserterPool[entityId].pickTarget)
                        || PipeRuntime.IsPipeObject(
                            factory,
                            system.inserterPool[entityId].insertTarget
                        )
                    );
            }
            int pending = -obj;
            if (
                pending >= factory.prebuildPool.Length
                || factory.prebuildPool[pending].id != pending
                || !(
                    LDB.items.Select(factory.prebuildPool[pending].protoId)?.prefabDesc.isInserter
                    ?? false
                )
            )
            {
                return false;
            }

            factory.ReadObjectConn(obj, 0, out _, out int a, out _);
            factory.ReadObjectConn(obj, 1, out _, out int b, out _);
            return PipeRuntime.IsPipeObject(factory, a) || PipeRuntime.IsPipeObject(factory, b);
        }

        public static FluidInserterMesh.Socket ResolveSocket(
            PlanetFactory factory,
            int target,
            int slot,
            Vector3 position,
            Vector3 toward
        )
        {
            var up = position.normalized;
            if (!PipeRuntime.IsPipeObject(factory, target))
            {
                if (target != 0)
                {
                    var proto =
                        target > 0
                            ? factory.entityPool[target].protoId
                            : factory.prebuildPool[-target].protoId;
                    var desc = LDB.items.Select(proto)?.prefabDesc;
                    var slots = desc?.slotPoses;
                    if (desc != null && desc.isBelt)
                    {
                        var forward = Vector3.ProjectOnPlane(toward - position, up).normalized;
                        if (forward.sqrMagnitude < 0.001f)
                        {
                            forward = Vector3.ProjectOnPlane(Vector3.forward, up).normalized;
                        }

                        var port = position + up * 0.60f + forward * 0.025f;
                        return new FluidInserterMesh.Socket
                        {
                            Belt = true,
                            Anchor = position,
                            Axis = up,
                            Normal = forward,
                            Center = port,
                            Position = port + forward * 0.375f + up * 0.20f,
                        };
                    }
                    if (slots != null && slots.Length > 0)
                    {
                        var origin =
                            target > 0
                                ? factory.entityPool[target].pos
                                : factory.prebuildPool[-target].pos;
                        var rot =
                            target > 0
                                ? factory.entityPool[target].rot
                                : factory.prebuildPool[-target].rot;
                        if (slot < 0 || slot >= slots.Length)
                        {
                            float best = float.MaxValue;
                            slot = 0;
                            for (int index = 0; index < slots.Length; index++)
                            {
                                float distance = (
                                    origin + rot * slots[index].position - position
                                ).sqrMagnitude;
                                if (distance < best)
                                {
                                    best = distance;
                                    slot = index;
                                }
                            }
                        }
                        var pose = slots[slot];
                        var machineUp = rot * pose.rotation * Vector3.up;
                        var machineNormal = rot * pose.rotation * Vector3.forward;
                        var anchor = origin + rot * pose.position;
                        var center =
                            anchor
                            + machineUp * 0.50f
                            + machineNormal * NativeInserterHousing.PortDepth;
                        return new FluidInserterMesh.Socket
                        {
                            Machine = true,
                            Center = center,
                            Normal = machineNormal,
                            Anchor = anchor,
                            Axis = machineUp,
                            Position = center + machineNormal * 0.40f + machineUp * 0.20f,
                        };
                    }
                }
                return new FluidInserterMesh.Socket { Position = position + up * 0.10f };
            }
            var rotation =
                target > 0 ? factory.entityPool[target].rot : factory.prebuildPool[-target].rot;
            var axis = rotation * Vector3.forward;
            foreach (int other in PipeTopology.Neighbors(factory, target))
            {
                if (other != 0 && PipeRuntime.IsPipeObject(factory, other))
                {
                    var center =
                        target > 0
                            ? factory.entityPool[target].pos
                            : factory.prebuildPool[-target].pos;
                    var peer =
                        other > 0
                            ? factory.entityPool[other].pos
                            : factory.prebuildPool[-other].pos;
                    if ((peer - center).sqrMagnitude > 0.001f)
                    {
                        axis = (peer - center).normalized;
                        break;
                    }
                }
            }

            var normal = Vector3.ProjectOnPlane(up, axis);
            if (normal.sqrMagnitude < 0.01f)
            {
                normal = Vector3.ProjectOnPlane(toward - position, axis);
            }

            if (normal.sqrMagnitude < 0.01f)
            {
                normal = Vector3.Cross(axis, Vector3.right);
            }

            if (normal.sqrMagnitude < 0.01f)
            {
                normal = Vector3.Cross(axis, Vector3.forward);
            }

            normal.Normalize();
            var pipeCenter = position + up * (PipeMesh.Radius + 0.06f);
            float neck = Mathf.Abs(Vector3.Dot(up, axis)) > 0.75f ? 0.22f : 0.14f;
            return new FluidInserterMesh.Socket
            {
                Clamp = true,
                Center = pipeCenter,
                Axis = axis,
                Normal = normal,
                Position = pipeCenter + normal * (PipeMesh.Radius + neck),
            };
        }

        public static void Refresh(PlanetFactory factory, int obj)
        {
            if (factory != current || obj == 0)
            {
                return;
            }

            if (!UsesPipe(factory, obj))
            {
                if (shapes.TryGetValue(obj, out var removed))
                {
                    removed.Free();
                    shapes.Remove(obj);
                }
                return;
            }
            if (!shapes.TryGetValue(obj, out var shape))
            {
                shapes.Add(obj, shape = new Shape());
            }

            Vector3 start;

            Vector3 end;
            int a;
            int b;
            int slotA;
            int slotB;
            factory.ReadObjectConn(obj, 1, out _, out _, out slotA);
            factory.ReadObjectConn(obj, 0, out _, out _, out slotB);
            if (obj > 0)
            {
                var entity = factory.entityPool[obj];
                var system = factory.factorySystem;
                var arm = system.inserterPool[entity.inserterId];
                start = entity.pos;
                end = system.inserterPosePool[entity.inserterId].pos2;
                a = arm.pickTarget;
                b = arm.insertTarget;
                shape.Grade = arm.grade;
            }
            else
            {
                var pending = factory.prebuildPool[-obj];
                start = pending.pos;
                end = pending.pos2;
                shape.Grade = LDB.items.Select(pending.protoId).prefabDesc.inserterGrade;
                factory.ReadObjectConn(obj, 1, out _, out a, out _);
                factory.ReadObjectConn(obj, 0, out _, out b, out _);
            }
            if ((end - start).sqrMagnitude > 0.01f)
            {
                shape.Set(
                    ResolveSocket(factory, a, slotA, start, end),
                    ResolveSocket(factory, b, slotB, end, start)
                );
            }
        }

        public static void Remove(PlanetFactory factory, int obj)
        {
            if (factory == current && shapes.TryGetValue(obj, out var shape))
            {
                shape.Free();
                shapes.Remove(obj);
            }
        }

        public static void Clear()
        {
            foreach (var shape in shapes.Values)
            {
                shape.Free();
            }

            shapes.Clear();
            foreach (var shape in previews)
            {
                shape.Free();
            }

            previews.Clear();
            current = null;
            previewFrame = -1;
            previewCount = 0;
        }

        public static void Dispose()
        {
            var manager = GameMain.gpuiManager;
            if (current != null && manager?.activeFactory == current)
            {
                foreach (int obj in shapes.Keys)
                {
                    if (obj > 0 && current.entityPool[obj].id == obj)
                    {
                        var entity = current.entityPool[obj];
                        if (entity.modelId > 0)
                        {
                            manager.AlterModel(
                                entity.modelIndex,
                                entity.modelId,
                                obj,
                                entity.pos,
                                entity.rot
                            );
                        }
                    }
                    else if (obj < 0 && current.prebuildPool[-obj].id == -obj)
                    {
                        var e = current.prebuildPool[-obj];
                        if (e.modelId > 0)
                        {
                            manager.AlterPrebuildModel(e.modelIndex, e.modelId, -obj, e.pos, e.rot);
                        }
                    }
                }
            }

            Clear();
            if (dark != null)
            {
                UnityEngine.Object.Destroy(dark);
            }

            if (preview != null)
            {
                UnityEngine.Object.Destroy(preview);
            }

            for (int index = 0; index < accents.Length; index++)
            {
                if (accents[index] != null)
                {
                    UnityEngine.Object.Destroy(accents[index]);
                }
                accents[index] = null;
            }
            dark = preview = null;
            NativeInserterHousing.Dispose();
        }

        private static void Materials()
        {
            if (dark != null)
            {
                return;
            }

            dark = new Material(PipeVisuals.JointMaterial)
            {
                name = "FluidPipes.InserterMotor",
                color = new Color(0.14f, 0.19f, 0.23f),
            };
            dark.SetFloat("_Metallic", 0.65f);
            dark.SetFloat("_Glossiness", 0.5f);
            preview = new Material(PipeVisuals.JointMaterial)
            {
                name = "FluidPipes.InserterPreview",
                color = new Color(0.28f, 0.82f, 0.94f),
            };
            preview.SetFloat("_Metallic", 0);
            preview.SetFloat("_Glossiness", 0.4f);
        }

        public static Material[] SurfaceMaterials
        {
            get
            {
                Materials();
                return new[]
                {
                    PipeVisuals.JointMaterial,
                    PipeVisuals.BodyMaterial,
                    dark,
                    GradeMaterial(3),
                };
            }
        }

        private static Material GradeMaterial(int grade)
        {
            grade = Math.Max(1, Math.Min(4, grade));
            if (accents[grade] != null)
            {
                return accents[grade];
            }

            var color =
                grade == 1 ? new Color(0.95f, 0.72f, 0.18f)
                : grade == 2 ? new Color(0.27f, 0.78f, 0.35f)
                : grade == 3 ? new Color(0.16f, 0.56f, 0.94f)
                : new Color(0.88f, 0.91f, 0.94f);
            var material = new Material(PipeVisuals.JointMaterial)
            {
                name = "FluidPipes.InserterGrade" + grade,
                color = color,
            };
            material.SetFloat("_Metallic", 0.30f);
            material.SetFloat("_Glossiness", 0.6f);
            accents[grade] = material;
            return material;
        }

        private static void DrawShape(Shape shape, Camera camera, bool ghost)
        {
            if (!shape.HasShape)
            {
                return;
            }

            shape.EnsureMesh();
            var bounds = shape.Mesh.bounds;
            bounds.center += shape.Origin;
            if (shape.Source.Machine || shape.Source.Belt)
            {
                bounds.Encapsulate(
                    new Bounds(
                        shape.Source.Anchor
                            + shape.Source.Axis * 0.5f
                            + shape.Source.Normal * 0.27f,
                        Vector3.one * 1.9f
                    )
                );
            }

            if (shape.Destination.Machine || shape.Destination.Belt)
            {
                bounds.Encapsulate(
                    new Bounds(
                        shape.Destination.Anchor
                            + shape.Destination.Axis * 0.5f
                            + shape.Destination.Normal * 0.27f,
                        Vector3.one * 1.9f
                    )
                );
            }

            if (!GeometryUtility.TestPlanesAABB(planes, bounds))
            {
                return;
            }

            var transform = Matrix4x4.Translate(shape.Origin);
            var lighting = PipeVisuals.SteelLighting(shape.Up);
            if (ghost)
            {
                previewProperties.SetColor(
                    "_Color",
                    shape.Error ? new Color(0.95f, 0.25f, 0.18f) : new Color(0.28f, 0.82f, 0.94f)
                );
                lighting = previewProperties;
            }
            for (int index = 0; index < 4; index++)
            {
                var material =
                    ghost ? preview
                    : index == 0 ? PipeVisuals.JointMaterial
                    : index == 1 ? PipeVisuals.BodyMaterial
                    : index == 2 ? dark
                    : GradeMaterial(shape.Grade);
                Graphics.DrawMesh(
                    shape.Mesh,
                    transform,
                    material,
                    0,
                    camera,
                    index,
                    lighting,
                    ShadowCastingMode.Off,
                    false
                );
            }
            DrawHousing(shape.Source, shape.Grade, camera, ghost);
            DrawHousing(shape.Destination, shape.Grade, camera, ghost);
        }

        private static void DrawHousing(
            FluidInserterMesh.Socket socket,
            int grade,
            Camera camera,
            bool ghost
        )
        {
            if (!socket.Machine && !socket.Belt)
            {
                return;
            }

            var housing = NativeInserterHousing.Get(grade, socket.Machine);
            var pose = Matrix4x4.TRS(
                socket.Anchor,
                Quaternion.LookRotation(socket.Normal, socket.Axis),
                Vector3.one
            );
            Graphics.DrawMesh(
                housing.Mesh,
                pose,
                ghost ? preview : housing.Material,
                0,
                camera,
                0,
                ghost ? previewProperties : null,
                ShadowCastingMode.Off,
                false
            );
        }

        public static void Draw(PlanetFactory factory, Camera camera)
        {
            if (factory?.factorySystem == null || camera == null)
            {
                return;
            }

            if (current != factory)
            {
                Clear();
                current = factory;
                var system = factory.factorySystem;
                for (int entityId2 = 1; entityId2 < system.inserterCursor; entityId2++)
                {
                    if (system.inserterPool[entityId2].id == entityId2)
                    {
                        Refresh(factory, system.inserterPool[entityId2].entityId);
                    }
                }

                for (int entityId3 = 1; entityId3 < factory.prebuildCursor; entityId3++)
                {
                    if (factory.prebuildPool[entityId3].id == entityId3)
                    {
                        Refresh(factory, -entityId3);
                    }
                }
            }
            Materials();
            GeometryUtility.CalculateFrustumPlanes(camera, planes);
            foreach (var entry in shapes)
            {
                DrawShape(entry.Value, camera, entry.Key < 0);
            }

            if (previewFrame == Time.frameCount)
            {
                for (int index = 0; index < previewCount; index++)
                {
                    DrawShape(previews[index], camera, true);
                }
            }
        }

        internal static bool IsPreview(PlanetFactory factory, BuildPreview item) =>
            item.desc != null
            && item.desc.isInserter
            && (
                PipeRuntime.IsPipeObject(factory, item.inputObjId)
                || PipeRuntime.IsPipeObject(factory, item.outputObjId)
                || item.input?.item?.ID == Prototypes.ItemId
                || item.output?.item?.ID == Prototypes.ItemId
            );

        internal static void Preview(BuildTool_Inserter tool)
        {
            previewFrame = Time.frameCount;
            previewCount = 0;
            foreach (var item in tool.buildPreviews)
            {
                if (IsPreview(tool.factory, item) && (item.lpos2 - item.lpos).sqrMagnitude > 0.01f)
                {
                    if (previewCount == previews.Count)
                    {
                        previews.Add(new Shape());
                    }

                    var shape = previews[previewCount++];
                    shape.Error = item.condition != EBuildCondition.Ok;
                    shape.Grade = item.desc.inserterGrade;
                    shape.Set(
                        ResolveSocket(
                            tool.factory,
                            item.inputObjId,
                            item.inputFromSlot,
                            item.lpos,
                            item.lpos2
                        ),
                        ResolveSocket(
                            tool.factory,
                            item.outputObjId,
                            item.outputToSlot,
                            item.lpos2,
                            item.lpos
                        )
                    );
                }
            }
        }

        internal static void Hide(InserterRenderer renderer, int index, int objId, bool setBuffer)
        {
            var factory = renderer.manager?.activeFactory;
            if (factory == null || index <= 0 || index >= renderer.instPool.Length)
            {
                return;
            }

            int obj = objId & 0xFFFFFF;
            if (renderer.isPrebuild)
            {
                obj = -obj;
            }

            Refresh(factory, obj);
            if (!UsesPipe(factory, obj))
            {
                return;
            }
            // Keep native instance ownership and collision data while clearing its visible GPU pose.
            renderer.instPool[index] = new GPUOBJECT
            {
                objId = renderer.instPool[index].objId,
                rotw = 1,
            };
            renderer.pose2Pool[index] = new ADDPOSE { rw = 1 };
            if (setBuffer)
            {
                ((ComputeBuffer)instBuffer.GetValue(renderer)).SetData(
                    renderer.instPool,
                    index,
                    index,
                    1
                );
                ((ComputeBuffer)poseBuffer.GetValue(renderer)).SetData(
                    renderer.pose2Pool,
                    index,
                    index,
                    1
                );
            }
        }
    }

    [HarmonyPatch]
    internal static class FluidInserterAdded
    {
        [HarmonyTargetMethods]
        public static IEnumerable<MethodBase> Targets()
        {
            foreach (
                var method in typeof(InserterRenderer).GetMethods(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly
                )
            )
            {
                if (method.Name == "AddInst")
                {
                    yield return method;
                }
            }
        }

        [HarmonyPostfix]
        public static void Added(
            InserterRenderer __instance,
            int objId,
            bool setBuffer,
            int __result
        ) => FluidInserterVisuals.Hide(__instance, __result, objId, setBuffer);
    }

    [HarmonyPatch]
    internal static class FluidInserterAltered
    {
        [HarmonyTargetMethods]
        public static IEnumerable<MethodBase> Targets()
        {
            foreach (
                var method in typeof(InserterRenderer).GetMethods(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly
                )
            )
            {
                if (method.Name == "AlterInst")
                {
                    yield return method;
                }
            }
        }

        [HarmonyPostfix]
        public static void Altered(
            InserterRenderer __instance,
            int index,
            int objId,
            bool setBuffer
        ) => FluidInserterVisuals.Hide(__instance, index, objId, setBuffer);
    }

    [HarmonyPatch]
    internal static class FluidInserterChanges
    {
        internal static void PendingConnection(PlanetFactory factory, int obj)
        {
            if (obj >= 0 || -obj >= factory.prebuildPool.Length)
            {
                return;
            }

            var pending = factory.prebuildPool[-obj];
            if (
                pending.id != -obj
                || !(LDB.items.Select(pending.protoId)?.prefabDesc.isInserter ?? false)
            )
            {
                return;
            }

            FluidInserterVisuals.Refresh(factory, obj);
            var manager = GameMain.gpuiManager;
            if (manager?.activeFactory == factory && pending.modelId > 0)
            {
                manager.AlterPrebuildModel(
                    pending.modelIndex,
                    pending.modelId,
                    -obj,
                    pending.pos,
                    pending.rot
                );
            }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(PlanetFactory), nameof(PlanetFactory.WriteObjectConn))]
        public static void Connection(PlanetFactory __instance, int objId, int otherObjId)
        {
            PendingConnection(__instance, objId);
            PendingConnection(__instance, otherObjId);
        }

        [
            HarmonyPostfix,
            HarmonyPatch(typeof(FactorySystem), nameof(FactorySystem.SetInserterPickTarget)),
            HarmonyPatch(typeof(FactorySystem), nameof(FactorySystem.SetInserterInsertTarget))
        ]
        public static void Target(FactorySystem __instance, int inserterId)
        {
            if (inserterId > 0 && inserterId < __instance.inserterPool.Length)
            {
                FluidInserterVisuals.Refresh(
                    __instance.factory,
                    __instance.inserterPool[inserterId].entityId
                );
            }
        }

        [
            HarmonyPrefix,
            HarmonyPatch(typeof(PlanetFactory), nameof(PlanetFactory.RemoveEntityWithComponents))
        ]
        public static void Removed(PlanetFactory __instance, int id) =>
            FluidInserterVisuals.Remove(__instance, id);

        [
            HarmonyPrefix,
            HarmonyPatch(typeof(PlanetFactory), nameof(PlanetFactory.RemovePrebuildWithComponents))
        ]
        public static void PendingRemoved(PlanetFactory __instance, int id) =>
            FluidInserterVisuals.Remove(__instance, -id);
    }

    [HarmonyPatch(
        typeof(PlanetFactory),
        nameof(PlanetFactory.ClearObjectConn),
        new[] { typeof(int), typeof(int) }
    )]
    internal static class FluidInserterDisconnected
    {
        [HarmonyPrefix]
        public static void Before(PlanetFactory __instance, int objId, int slot, out int __state)
        {
            __instance.ReadObjectConn(objId, slot, out _, out __state, out _);
        }

        [HarmonyPostfix]
        public static void After(PlanetFactory __instance, int objId, int __state)
        {
            FluidInserterChanges.PendingConnection(__instance, objId);
            FluidInserterChanges.PendingConnection(__instance, __state);
        }
    }

    [HarmonyPatch(typeof(BuildTool_Inserter), nameof(BuildTool_Inserter.UpdatePreviewModels))]
    internal static class FluidInserterPreview
    {
        [HarmonyPrefix]
        public static void Before(BuildTool_Inserter __instance, out List<BuildPreview> __state)
        {
            __state = null;
            foreach (var item in __instance.buildPreviews)
            {
                if (item.needModel && FluidInserterVisuals.IsPreview(__instance.factory, item))
                {
                    if (__state == null)
                    {
                        __state = new List<BuildPreview>();
                    }
                    __state.Add(item);
                    item.needModel = false;
                    item.previewIndex = -1;
                }
            }
        }

        [HarmonyPostfix]
        public static void After(BuildTool_Inserter __instance, List<BuildPreview> __state)
        {
            if (__state != null)
            {
                foreach (var item in __state)
                {
                    item.needModel = true;
                }
            }
            FluidInserterVisuals.Preview(__instance);
        }
    }
}
