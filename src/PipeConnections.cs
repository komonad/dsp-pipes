using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace FluidPipes
{
    internal static class PipeConnections
    {
        private static readonly MethodInfo WriteDirect = AccessTools.Method(
            typeof(PlanetFactory),
            "WriteObjectConnDirect"
        );

        private static int SlotCount(PlanetFactory factory, int objectId)
        {
            // 连接存储跟随已分配的属主池；大型改版 mod 可扩展其步长。
            return objectId > 0
                ? factory.entityConnPool.Length / factory.entityPool.Length
                : factory.prebuildConnPool.Length / factory.prebuildPool.Length;
        }

        public static void MoveObject(PlanetFactory factory, int oldId, int newId)
        {
            PipeTopology.Move(factory, oldId, newId);
            int slotCount = SlotCount(factory, oldId);
            for (int slot = 0; slot < slotCount; slot++)
            {
                factory.ReadObjectConn(
                    oldId,
                    slot,
                    out bool output,
                    out int other,
                    out int otherSlot
                );
                if (other == 0)
                {
                    continue;
                }

                factory.ReadObjectConn(other, otherSlot, out _, out int back, out int backSlot);
                if (back != oldId || backSlot != slot)
                {
                    continue;
                }

                WriteDirect.Invoke(factory, new object[] { newId, slot, output, other, otherSlot });
                WriteDirect.Invoke(
                    factory,
                    new object[] { other, otherSlot, !output, newId, slot }
                );
            }
            Array.Clear(
                oldId > 0 ? factory.entityConnPool : factory.prebuildConnPool,
                Math.Abs(oldId) * slotCount,
                slotCount
            );
        }

        private static bool Reciprocal(
            PlanetFactory factory,
            int entityId,
            int slot,
            int other,
            int otherSlot
        )
        {
            if (other == 0 || otherSlot < 0 || otherSlot >= SlotCount(factory, other))
            {
                return false;
            }

            if (
                other > 0
                    ? (other >= factory.entityPool.Length || factory.entityPool[other].id != other)
                    : (
                        -other >= factory.prebuildPool.Length
                        || factory.prebuildPool[-other].id != -other
                    )
            )
            {
                return false;
            }

            factory.ReadObjectConn(other, otherSlot, out _, out int back, out int backSlot);
            return back == entityId && backSlot == slot;
        }

        internal static bool TryReadNativeLink(
            PlanetFactory factory,
            int entityId,
            int slot,
            out bool output,
            out int otherEntityId
        )
        {
            output = false;
            otherEntityId = 0;
            if (slot < 0 || slot >= 4 || !PipeRuntime.IsPipeObject(factory, entityId))
            {
                return false;
            }

            factory.ReadObjectConn(entityId, slot, out output, out int other, out int otherSlot);
            if (
                !PipeRuntime.IsPipeObject(factory, other)
                || otherSlot < 0
                || otherSlot >= 4
                || !PipeTypeRegistry.Compatible(
                    PipeRuntime.ObjectPrototype(factory, entityId),
                    PipeRuntime.ObjectPrototype(factory, other)
                )
                || !Reciprocal(factory, entityId, slot, other, otherSlot)
            )
            {
                return false;
            }

            factory.ReadObjectConn(other, otherSlot, out bool otherOutput, out _, out _);
            if (otherOutput == output)
            {
                return false;
            }

            if (
                !PipeTypeRegistry.IsUndirected(PipeRuntime.ObjectPrototype(factory, entityId))
                && (output != (slot == 0) || otherOutput != (otherSlot == 0))
            )
            {
                return false;
            }

            otherEntityId = other;
            return true;
        }

        public static void RepairImported(PlanetFactory factory, bool legacy)
        {
            var graph = PipeRuntime.Bind(factory.cargoTraffic);
            int slots = 0;
            int edges = 0;
            for (int entityId = 1; entityId < factory.entityCursor; entityId++)
            {
                if (!PipeRuntime.IsPipeObject(factory, entityId))
                {
                    continue;
                }

                for (int slot = 0; slot < 4; slot++)
                {
                    factory.ReadObjectConn(entityId, slot, out _, out int other, out int otherSlot);
                    if (other != 0 && !Reciprocal(factory, entityId, slot, other, otherSlot))
                    {
                        factory.entityConnPool[entityId * SlotCount(factory, entityId) + slot] = 0;
                        slots++;
                    }
                }
                if (!legacy)
                {
                    continue;
                }

                foreach (int other in PipeTopology.Neighbors(factory, entityId))
                {
                    bool invalid = !PipeRuntime.IsPipeObject(factory, other);
                    if (!invalid && other > entityId)
                    {
                        // 原生路径放置会为超过一格间距的跨度插入中间节点。
                        float limit = 2.2f * Math.Max(1, factory.planet.realRadius / 200f);
                        if (
                            (
                                factory.entityPool[entityId].pos - factory.entityPool[other].pos
                            ).sqrMagnitude
                            > limit * limit
                        )
                        {
                            bool backed = false;
                            for (int slot = 0; slot < 4; slot++)
                            {
                                factory.ReadObjectConn(
                                    entityId,
                                    slot,
                                    out _,
                                    out int peer,
                                    out int peerSlot
                                );
                                if (
                                    peer == other
                                    && Reciprocal(factory, entityId, slot, other, peerSlot)
                                )
                                {
                                    backed = true;
                                    break;
                                }
                            }
                            invalid = !backed;
                        }
                    }
                    if (!invalid)
                    {
                        continue;
                    }

                    PipeTopology.Disconnect(factory, entityId, other);
                    if (other > 0)
                    {
                        graph.Disconnect(entityId, other);
                    }

                    edges++;
                }
            }
            if (slots + edges > 0)
            {
                UnityEngine.Debug.Log(
                    "FluidPipes restored connection consistency: stale slots="
                        + slots
                        + ", invalid edges="
                        + edges
                );
            }
        }

        public static int FreeSlot(PlanetFactory factory, int entityId, int preferred = 0)
        {
            if (preferred >= 0 && preferred < 4)
            {
                factory.ReadObjectConn(entityId, preferred, out _, out int other, out _);
                if (other == 0)
                {
                    return preferred;
                }
            }
            for (int slot = 1; slot < 4; slot++)
            {
                factory.ReadObjectConn(entityId, slot, out _, out int other, out _);
                if (other == 0)
                {
                    return slot;
                }
            }
            factory.ReadObjectConn(entityId, 0, out _, out int output, out _);
            return output == 0 ? 0 : -1;
        }

        public static bool Connect(
            PlanetFactory factory,
            int firstEntityId,
            int secondEntityId,
            int preferredA,
            int preferredB
        )
        {
            if (
                firstEntityId == secondEntityId
                || !PipeTypeRegistry.IsUndirected(
                    PipeRuntime.ObjectPrototype(factory, firstEntityId)
                )
                || !PipeRuntime.CanLink(factory, firstEntityId, secondEntityId)
                || PipeRuntime.HasInventory(factory, firstEntityId)
                    && !PipeTopology.Compatible(factory, firstEntityId, secondEntityId)
            )
            {
                return false;
            }

            if (Array.IndexOf(PipeTopology.Neighbors(factory, firstEntityId), secondEntityId) >= 0)
            {
                return true;
            }

            int sa = FreeSlot(factory, firstEntityId, preferredA);

            int sb = FreeSlot(factory, secondEntityId, preferredB);
            PipeTopology.Connect(factory, firstEntityId, secondEntityId);
            // 零号槽位是原生路径输出；额外的无向边使用两端空闲的输入槽位。
            if (sa == 0 && sb == 0)
            {
                sb = FreeSlot(factory, secondEntityId, 1);
            }

            if (sa > 0 && sb > 0)
            {
                factory.ReadObjectConn(firstEntityId, 0, out _, out int oa, out _);
                factory.ReadObjectConn(secondEntityId, 0, out _, out int ob, out _);
                if (oa == 0)
                {
                    sa = 0;
                }
                else if (ob == 0)
                {
                    sb = 0;
                }
            }
            if (sa >= 0 && sb >= 0)
            {
                bool output = sa == 0 || sb != 0;
                WriteDirect.Invoke(
                    factory,
                    new object[] { firstEntityId, sa, output, secondEntityId, sb }
                );
                WriteDirect.Invoke(
                    factory,
                    new object[] { secondEntityId, sb, !output, firstEntityId, sa }
                );
            }
            Reconcile(factory, firstEntityId);
            Reconcile(factory, secondEntityId);
            return true;
        }

        public static bool Disconnect(PlanetFactory factory, int firstEntityId, int secondEntityId)
        {
            if (
                firstEntityId == secondEntityId
                || !PipeRuntime.IsPipeObject(factory, firstEntityId)
                || !PipeRuntime.IsPipeObject(factory, secondEntityId)
            )
            {
                return false;
            }

            if (firstEntityId > 0)
            {
                PipeRuntime.UseObjectLinks(factory.cargoTraffic, firstEntityId);
            }

            if (secondEntityId > 0)
            {
                PipeRuntime.UseObjectLinks(factory.cargoTraffic, secondEntityId);
            }

            bool undirected = PipeTypeRegistry.IsUndirected(
                PipeRuntime.ObjectPrototype(factory, firstEntityId)
            );
            bool connected = undirected
                ? PipeTopology.Adjacent(factory, firstEntityId, secondEntityId)
                : NativeAdjacent(factory, firstEntityId, secondEntityId);
            if (!connected)
            {
                return false;
            }

            PipeTopology.Disconnect(factory, firstEntityId, secondEntityId);
            foreach (int entityId in new[] { firstEntityId, secondEntityId })
            {
                for (int slot = 0; slot < 4; slot++)
                {
                    factory.ReadObjectConn(entityId, slot, out _, out int other, out _);
                    if (other == (entityId == firstEntityId ? secondEntityId : firstEntityId))
                    {
                        factory.ClearObjectConn(entityId, slot);
                    }
                }
            }

            if (firstEntityId > 0 && secondEntityId > 0)
            {
                PipeRuntime.Bind(factory.cargoTraffic).Disconnect(firstEntityId, secondEntityId);
            }

            if (!undirected)
            {
                RebuildNativePath(factory, firstEntityId);
                RebuildNativePath(factory, secondEntityId);
            }

            Reconcile(factory, firstEntityId);
            Reconcile(factory, secondEntityId);
            PipeVisuals.Dirty(factory, firstEntityId);
            PipeVisuals.Dirty(factory, secondEntityId);
            return true;
        }

        private static bool NativeAdjacent(PlanetFactory factory, int entityId, int otherEntityId)
        {
            for (int slot = 0; slot < 4; slot++)
            {
                if (
                    TryReadNativeLink(factory, entityId, slot, out _, out int neighbor)
                    && neighbor == otherEntityId
                )
                {
                    return true;
                }
            }
            return false;
        }

        private static void RebuildNativePath(PlanetFactory factory, int entityId)
        {
            if (entityId <= 0 || !PipeRuntime.IsPipeObject(factory, entityId))
            {
                return;
            }

            int beltId = factory.entityPool[entityId].beltId;
            if (!PipeRuntime.IsPipe(factory.cargoTraffic, beltId))
            {
                return;
            }

            NativeLinks(
                factory.cargoTraffic,
                entityId,
                out int output,
                out int input0,
                out int input1,
                out int input2
            );
            factory.cargoTraffic.AlterBeltConnections(beltId, output, input0, input1, input2);
        }

        public static void NativeLinks(
            CargoTraffic traffic,
            int entity,
            out int output,
            out int i0,
            out int i1,
            out int i2
        )
        {
            output = i0 = i1 = i2 = 0;
            var factory = traffic.factory;
            for (int slot = 0; slot < 4; slot++)
            {
                factory.ReadObjectConn(entity, slot, out _, out int other, out int otherSlot);
                if (other <= 0 || !TryReadNativeLink(factory, entity, slot, out _, out other))
                {
                    continue;
                }

                int belt = factory.entityPool[other].beltId;
                if (belt <= 0)
                {
                    continue;
                }

                if (slot == 0 && otherSlot > 0 && otherSlot < 4)
                {
                    output = belt;
                }

                if (slot > 0 && otherSlot == 0)
                {
                    if (i0 == 0)
                    {
                        i0 = belt;
                    }
                    else if (i1 == 0)
                    {
                        i1 = belt;
                    }
                    else
                    {
                        i2 = belt;
                    }
                }
            }
        }

        public static void Reconcile(PlanetFactory factory, int entity)
        {
            if (entity <= 0 || !PipeRuntime.IsPipeObject(factory, entity))
            {
                return;
            }

            var traffic = factory.cargoTraffic;
            int belt = factory.entityPool[entity].beltId;
            if (belt <= 0 || !PipeRuntime.IsPipe(traffic, belt))
            {
                return;
            }

            PipeRuntime.UseObjectLinks(traffic, entity);
            PipeRuntime.Sync(traffic, belt);
            if (
                traffic.beltPool[belt].segPathId > 0
                && PipeTypeRegistry.IsUndirected(factory.entityPool[entity].protoId)
            )
            {
                traffic.AlterBeltConnections(belt, 0, 0, 0, 0);
            }

            PipeVisuals.Dirty(factory, entity);
            API.PipeTypes.Changed(factory, entity);
        }
    }

    [HarmonyPatch]
    internal static class UndirectedConnections
    {
        [
            HarmonyPostfix,
            HarmonyPatch(typeof(BuildTool_Path), nameof(BuildTool_Path.DeterminePreviews))
        ]
        public static void Previews(BuildTool_Path __instance)
        {
            if (!PipeTypeRegistry.IsUndirected(__instance.handItem?.ID))
            {
                return;
            }

            foreach (var preview in __instance.buildPreviews)
            {
                preview.needModel = false;
                preview.isConnNode = true;
                if (PipeRuntime.IsPipeObject(__instance.factory, preview.coverObjId))
                {
                    preview.willRemoveCover = false;
                }
            }
            int count = __instance.buildPreviews.Count;
            if (count > 1)
            {
                if (PipeRuntime.IsPipeObject(__instance.factory, __instance.startObjectId))
                {
                    __instance.buildPreviews[0].lpos = __instance.buildPreviews[0].lpos2 =
                        PipePlanning.Position(__instance.factory, __instance.startObjectId);
                }

                if (PipeRuntime.IsPipeObject(__instance.factory, __instance.castObjectId))
                {
                    __instance.buildPreviews[count - 1].lpos = __instance
                        .buildPreviews[count - 1]
                        .lpos2 = PipePlanning.Position(__instance.factory, __instance.castObjectId);
                }
            }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(BuildTool_Path), nameof(BuildTool_Path.UpdateGizmos))]
        public static void Gizmos(BuildTool_Path __instance, BuildModel model)
        {
            if (!PipeTypeRegistry.IsUndirected(__instance.handItem?.ID))
            {
                return;
            }

            if (
                PipeRuntime.IsPipeObject(__instance.factory, __instance.startObjectId)
                && model.startGizmoDesc != null
            )
            {
                model.startGizmoDesc.showPorts = false;
                model.startGizmoDesc.showBody = false;
            }
            if (
                PipeRuntime.IsPipeObject(__instance.factory, __instance.castObjectId)
                && model.endGizmoDesc != null
            )
            {
                model.endGizmoDesc.showPorts = false;
                model.endGizmoDesc.showBody = false;
            }
        }

        [
            HarmonyPrefix,
            HarmonyPriority(Priority.First),
            HarmonyPatch(typeof(PlanetFactory), nameof(PlanetFactory.WriteObjectConn))
        ]
        public static bool Write(
            PlanetFactory __instance,
            int objId,
            int slot,
            int otherObjId,
            int otherSlot
        )
        {
            if (
                !PipeRuntime.IsPipeObject(__instance, objId)
                || !PipeRuntime.IsPipeObject(__instance, otherObjId)
                || !PipeTypeRegistry.IsUndirected(PipeRuntime.ObjectPrototype(__instance, objId))
            )
            {
                return true;
            }

            PipeConnections.Connect(__instance, objId, otherObjId, slot, otherSlot);
            return false;
        }

        [HarmonyPostfix, HarmonyPatch(typeof(PlanetFactory), nameof(PlanetFactory.WriteObjectConn))]
        public static void Written(PlanetFactory __instance, int objId, int otherObjId)
        {
            RefreshDirected(__instance, objId);
            RefreshDirected(__instance, otherObjId);
        }

        private static void RefreshDirected(PlanetFactory factory, int entityId)
        {
            if (
                entityId > 0
                && PipeRuntime.IsPipeObject(factory, entityId)
                && !PipeTypeRegistry.IsUndirected(PipeRuntime.ObjectPrototype(factory, entityId))
            )
            {
                PipeConnections.Reconcile(factory, entityId);
            }
        }

        [HarmonyPrefix, HarmonyPatch(typeof(PlanetFactory), "ClearObjectConnDirect")]
        public static void Clear(PlanetFactory __instance, int objId, int slot, out int __state)
        {
            __state = 0;
            if (slot >= 4 || !PipeRuntime.IsPipeObject(__instance, objId))
            {
                return;
            }

            __instance.ReadObjectConn(objId, slot, out _, out int other, out _);
            __state = other;
            if (!PipeRuntime.IsPipeObject(__instance, other))
            {
                return;
            }

            PipeTopology.Disconnect(__instance, objId, other);
            if (objId > 0 && other > 0)
            {
                PipeRuntime.Bind(__instance.cargoTraffic).Disconnect(objId, other);
            }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(PlanetFactory), "ClearObjectConnDirect")]
        public static void Cleared(PlanetFactory __instance, int objId, int __state)
        {
            if (__state != 0)
            {
                RefreshDirected(__instance, objId);
                RefreshDirected(__instance, __state);
                PipeVisuals.Dirty(__instance, objId);
                PipeVisuals.Dirty(__instance, __state);
                API.PipeTypes.Changed(__instance, objId);
                API.PipeTypes.Changed(__instance, __state);
            }
        }

        [
            HarmonyPrefix,
            HarmonyPatch(
                typeof(PlanetFactory),
                nameof(PlanetFactory.ClearObjectConn),
                new[] { typeof(int), typeof(int) }
            )
        ]
        public static void BeforeClear(
            PlanetFactory __instance,
            int objId,
            int slot,
            out int __state
        )
        {
            __instance.ReadObjectConn(objId, slot, out _, out __state, out _);
        }

        [
            HarmonyPostfix,
            HarmonyPatch(
                typeof(PlanetFactory),
                nameof(PlanetFactory.ClearObjectConn),
                new[] { typeof(int), typeof(int) }
            )
        ]
        public static void AfterClear(PlanetFactory __instance, int objId, int __state)
        {
            PipeConnections.Reconcile(__instance, objId);
            PipeConnections.Reconcile(__instance, __state);
        }

        [
            HarmonyPrefix,
            HarmonyPatch(
                typeof(PlanetFactory),
                nameof(PlanetFactory.HandleObjectConnChangeWhenBuild)
            )
        ]
        public static bool Built(PlanetFactory __instance, int oldId, int newId)
        {
            if (!PipeTypeRegistry.IsUndirected(PipeRuntime.ObjectPrototype(__instance, oldId)))
            {
                return true;
            }
            // 待建对象转实体时保留两端互为对应的槽位标识。
            PipeConnections.MoveObject(__instance, oldId, newId);
            return false;
        }

        [
            HarmonyPostfix,
            HarmonyPatch(typeof(PlanetFactory), nameof(PlanetFactory.RemovePrebuildWithComponents))
        ]
        public static void RemovedPrebuild(PlanetFactory __instance, int id)
        {
            PipeTopology.Remove(__instance, -id);
        }
    }

    [HarmonyPatch]
    internal static class PipeBuildConditions
    {
        [HarmonyTargetMethods]
        public static IEnumerable<MethodBase> Targets()
        {
            yield return AccessTools.Method(typeof(BuildTool_Path), "DeterminePreviews");
            yield return AccessTools.Method(typeof(BuildTool_Path), "CheckBuildConditions");
        }

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpile(
            IEnumerable<CodeInstruction> instructions
        )
        {
            var field = AccessTools.Field(typeof(BuildPreview), "condition");
            foreach (var instruction in instructions)
            {
                if (
                    (instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt)
                    && instruction.operand is MethodInfo method
                    && method.DeclaringType == typeof(UnityEngine.Physics)
                    && (
                        method.Name == nameof(UnityEngine.Physics.OverlapSphereNonAlloc)
                        || method.Name == nameof(UnityEngine.Physics.OverlapCapsuleNonAlloc)
                    )
                )
                {
                    var load = new CodeInstruction(OpCodes.Ldarg_0);
                    load.labels.AddRange(instruction.labels);
                    load.blocks.AddRange(instruction.blocks);
                    yield return load;
                    yield return new CodeInstruction(
                        OpCodes.Call,
                        AccessTools.Method(
                            typeof(PipeBuildConditions),
                            method.Name == nameof(UnityEngine.Physics.OverlapSphereNonAlloc)
                                ? nameof(Sphere)
                                : nameof(Capsule)
                        )
                    );
                    continue;
                }
                if (
                    instruction.Calls(
                        AccessTools.Method(
                            typeof(PlanetPhysics),
                            nameof(PlanetPhysics.GetColliderData),
                            new[]
                            {
                                typeof(UnityEngine.Collider),
                                typeof(ColliderData).MakeByRefType(),
                            }
                        )
                    )
                )
                {
                    var load = new CodeInstruction(OpCodes.Ldarg_0);
                    load.labels.AddRange(instruction.labels);
                    load.blocks.AddRange(instruction.blocks);
                    yield return load;
                    yield return new CodeInstruction(
                        OpCodes.Call,
                        AccessTools.Method(typeof(PipeBuildConditions), nameof(Collider))
                    );
                    continue;
                }
                if (instruction.opcode == OpCodes.Stfld && Equals(instruction.operand, field))
                {
                    var load = new CodeInstruction(OpCodes.Ldarg_0);
                    load.labels.AddRange(instruction.labels);
                    load.blocks.AddRange(instruction.blocks);
                    yield return load;
                    yield return new CodeInstruction(
                        OpCodes.Call,
                        AccessTools.Method(typeof(PipeBuildConditions), nameof(SetCondition))
                    );
                }
                else
                {
                    yield return instruction;
                }
            }
        }

        public static bool Collider(
            PlanetPhysics physics,
            UnityEngine.Collider collider,
            out ColliderData data,
            BuildTool_Path tool
        )
        {
            if (!physics.GetColliderData(collider, out data))
            {
                return false;
            }

            if (!PipeTypeRegistry.UsesPipeConstruction(tool.handItem?.ID))
            {
                return true;
            }

            int entityId =
                data.objType == EObjectType.None ? data.objId
                : data.objType == EObjectType.Prebuild ? -data.objId
                : 0;
            if (!PipeRuntime.IsPipeObject(tool.factory, entityId))
            {
                return true;
            }

            if (AllowedContact(tool, entityId))
            {
                return false;
            }

            return true;
        }

        private static bool JunctionMember(BuildTool_Path tool, int endpoint, int entityId) =>
            endpoint != 0
            && PipeRuntime.IsPipeObject(tool.factory, endpoint)
            && (entityId == endpoint || PipeTopology.Adjacent(tool.factory, endpoint, entityId));

        private static bool AllowedContact(BuildTool_Path tool, int entityId)
        {
            if (
                PipeTypeRegistry.IsUndirected(tool.handItem?.ID)
                && (
                    JunctionMember(tool, tool.startObjectId, entityId)
                    || JunctionMember(tool, tool.castObjectId, entityId)
                )
            )
            {
                return true;
            }
            foreach (var preview in tool.buildPreviews)
            {
                if (
                    JunctionMember(tool, preview.inputObjId, entityId)
                    || JunctionMember(tool, preview.outputObjId, entityId)
                )
                {
                    return true;
                }
            }
            return false;
        }

        private static int Filter(BuildTool_Path tool, UnityEngine.Collider[] colliders, int count)
        {
            if (
                !PipeTypeRegistry.UsesPipeConstruction(tool.handItem?.ID)
                || tool.planet?.physics == null
            )
            {
                return count;
            }

            int kept = 0;
            for (int index = 0; index < count; index++)
            {
                var collider = colliders[index];
                if (tool.planet.physics.GetColliderData(collider, out var data))
                {
                    int entityId =
                        data.objType == EObjectType.None ? data.objId
                        : data.objType == EObjectType.Prebuild ? -data.objId
                        : 0;
                    if (
                        PipeRuntime.IsPipeObject(tool.factory, entityId)
                        && (AllowedContact(tool, entityId))
                    )
                    {
                        continue;
                    }
                }
                colliders[kept++] = collider;
            }
            Array.Clear(colliders, kept, count - kept);
            return kept;
        }

        public static int Sphere(
            UnityEngine.Vector3 pos,
            float radius,
            UnityEngine.Collider[] results,
            int mask,
            UnityEngine.QueryTriggerInteraction query,
            BuildTool_Path tool
        )
        {
            if (PipePlanning.TryCollisionGeometry(tool, mask, out var geometry))
            {
                pos = PipePlanning.CollisionCenter(pos, geometry);
                radius = PipePlanning.CollisionRadius(geometry);
                if (tool.buildPreviews.Count == 1)
                {
                    var direction = tool.buildPreviews[0].lrot * UnityEngine.Vector3.forward;
                    float halfLength = geometry.IsolatedHalfLength;
                    return Filter(
                        tool,
                        results,
                        UnityEngine.Physics.OverlapCapsuleNonAlloc(
                            pos - direction * halfLength,
                            pos + direction * halfLength,
                            radius,
                            results,
                            mask,
                            query
                        )
                    );
                }
            }
            return Filter(
                tool,
                results,
                UnityEngine.Physics.OverlapSphereNonAlloc(pos, radius, results, mask, query)
            );
        }

        public static int Capsule(
            UnityEngine.Vector3 start,
            UnityEngine.Vector3 end,
            float radius,
            UnityEngine.Collider[] results,
            int mask,
            UnityEngine.QueryTriggerInteraction query,
            BuildTool_Path tool
        )
        {
            if (PipePlanning.TryCollisionGeometry(tool, mask, out var geometry))
            {
                start = PipePlanning.CollisionCenter(start, geometry);
                end = PipePlanning.CollisionCenter(end, geometry);
                radius = PipePlanning.CollisionRadius(geometry);
            }
            return Filter(
                tool,
                results,
                UnityEngine.Physics.OverlapCapsuleNonAlloc(start, end, radius, results, mask, query)
            );
        }

        public static void SetCondition(
            BuildPreview preview,
            EBuildCondition condition,
            BuildTool_Path tool
        )
        {
            if (
                PipeTypeRegistry.IsUndirected(tool.handItem?.ID)
                && DirectionOnly(condition)
                && (
                    HasRoom(tool)
                    || tool.startObjectId == 0 && tool.castObjectId == 0 && GeometryOnly(condition)
                )
            )
            {
                condition = EBuildCondition.Ok;
            }

            preview.condition = condition;
        }

        private static bool DirectionOnly(EBuildCondition condition) =>
            condition == EBuildCondition.Occupied
            || condition == EBuildCondition.InputFull
            || condition == EBuildCondition.InputConflict
            || GeometryOnly(condition);

        private static bool GeometryOnly(EBuildCondition condition) =>
            condition == EBuildCondition.TooBendInBeltJunction
            || condition == EBuildCondition.TooBend
            || condition == EBuildCondition.TooBendToLift
            || condition == EBuildCondition.JointCannotLift
            || condition == EBuildCondition.TooSteep;

        private static bool HasRoom(BuildTool_Path tool)
        {
            bool pipe = false;
            foreach (int entityId in new[] { tool.startObjectId, tool.castObjectId })
            {
                if (entityId == 0)
                {
                    continue;
                }

                if (PipeRuntime.IsPipeObject(tool.factory, entityId))
                {
                    pipe = true;
                }
                else
                {
                    foreach (var preview in tool.buildPreviews)
                    {
                        int slot =
                            preview.inputObjId == entityId ? preview.inputFromSlot
                            : preview.outputObjId == entityId ? preview.outputToSlot
                            : -1;
                        if (slot >= 0)
                        {
                            tool.factory.ReadObjectConn(entityId, slot, out _, out int peer, out _);
                            if (peer != 0)
                            {
                                return false;
                            }
                        }
                    }
                }
            }
            return pipe;
        }
    }

    [HarmonyPatch(typeof(BuildTool_Path), nameof(BuildTool_Path.CreatePrebuilds))]
    internal static class PipePlacementCollection
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpile(
            IEnumerable<CodeInstruction> instructions
        )
        {
            foreach (var instruction in instructions)
            {
                if (
                    instruction.Calls(
                        AccessTools.Method(typeof(GC), nameof(GC.Collect), Type.EmptyTypes)
                    )
                )
                {
                    var load = new CodeInstruction(OpCodes.Ldarg_0);
                    load.labels.AddRange(instruction.labels);
                    load.blocks.AddRange(instruction.blocks);
                    yield return load;
                    yield return new CodeInstruction(
                        OpCodes.Call,
                        AccessTools.Method(typeof(PipePlacementCollection), nameof(Collect))
                    );
                }
                else
                {
                    yield return instruction;
                }
            }
        }

        public static void Collect(BuildTool_Path tool)
        {
            if (!PipeTypeRegistry.IsItem(tool.handItem?.ID))
            {
                GC.Collect();
            }
        }
    }
}
