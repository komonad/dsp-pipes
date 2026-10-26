using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using BepInEx.Configuration;
using FluidPipes;
using FluidPipes.API;
using HarmonyLib;
using UnityEngine;
using xiaoye97;

public sealed partial class RuntimeChecks
{
    private const string TypeCheckOwner = "local.fluidpipes.checks";
    private const string StructuralCheckFamily = TypeCheckOwner + ":structural-family";
    private readonly Dictionary<string, ItemProto> typeCheckItems =
        new Dictionary<string, ItemProto>();
    private bool typePreAddCompleted;
    private bool typePostAddCompleted;

    private void SubscribePipeTypeChecks()
    {
        LDBTool.PreAddDataAction += PreparePipeTypeChecks;
        LDBTool.PostAddDataAction += CompletePipeTypeChecks;
    }

    private void OnDestroy()
    {
        LDBTool.PreAddDataAction -= PreparePipeTypeChecks;
        LDBTool.PostAddDataAction -= CompletePipeTypeChecks;
    }

    private void PreparePipeTypeChecks()
    {
        Test("pipe icon API registration", PrepareIconApiChecks);
        Test(
            "pipe type pre-add registration",
            () =>
            {
                var occupiedIds = new HashSet<int>(
                    LDB.items.dataArray.Select(prototype => prototype.ID)
                );
                foreach (string queueName in new[] { "PreToAdd", "PostToAdd" })
                {
                    var queuedGroups =
                        AccessTools.Field(typeof(LDBTool), queueName)?.GetValue(null)
                        as IEnumerable;
                    if (queuedGroups == null)
                    {
                        continue;
                    }

                    foreach (IEnumerable queuedGroup in queuedGroups)
                    {
                        foreach (object queuedPrototype in queuedGroup)
                        {
                            if (queuedPrototype is ItemProto item)
                            {
                                occupiedIds.Add(item.ID);
                            }
                        }
                    }
                }

                int nextItemId = 11800;
                foreach (
                    string localName in new[]
                    {
                        "shared-variant",
                        "isolated-inventory",
                        "structural-frame",
                        "other-structure",
                        "directed-track",
                        "structural-upgrade",
                        "structural-interior",
                        "directed-interior",
                        "open-beam",
                    }
                )
                {
                    while (occupiedIds.Contains(nextItemId) && nextItemId < 12000)
                    {
                        nextItemId++;
                    }

                    if (nextItemId >= 12000)
                    {
                        throw new InvalidOperationException(
                            "Pipe type fixture item IDs are exhausted."
                        );
                    }

                    var prototype = PipeTypes.CreateItem(
                        nextItemId,
                        "FluidPipes.Checks." + localName,
                        "FluidPipes.Description"
                    );
                    prototype.GridIndex = 0;
                    typeCheckItems.Add(localName, prototype);
                    occupiedIds.Add(nextItemId++);
                    var customIds = (ConfigFile)
                        AccessTools.Field(typeof(LDBTool), "CustomID").GetValue(null);
                    customIds.Bind("Item", prototype.Name, prototype.ID).Value = prototype.ID;
                    LDBTool.PreAddProto(prototype);
                }

                PipeTypes.Register(
                    TypeCheckOwner,
                    new PipeTypeDefinition(
                        "shared-variant",
                        typeCheckItems["shared-variant"],
                        TypeCheckAppearance("shared-variant"),
                        geometry: new PipeGeometryProfile(radius: 0.46f, centerHeight: 0.6f)
                    )
                );
                PipeTypes.Register(
                    TypeCheckOwner,
                    new PipeTypeDefinition(
                        "isolated-inventory",
                        typeCheckItems["isolated-inventory"],
                        TypeCheckAppearance("isolated-inventory", failEffects: true),
                        connectionFamily: TypeCheckOwner + ":isolated-inventory"
                    )
                );
                PipeTypes.Register(
                    TypeCheckOwner,
                    new PipeTypeDefinition(
                        "structural-frame",
                        typeCheckItems["structural-frame"],
                        new PipeAppearance(
                            PipeWallMode.Opaque,
                            hasCore: true,
                            textureCoordinate: position => position.x * 0.25f + position.z * 0.5f
                        ),
                        PipeBehavior.Structure,
                        StructuralCheckFamily
                    )
                );
                PipeTypes.Register(
                    TypeCheckOwner,
                    new PipeTypeDefinition(
                        "structural-upgrade",
                        typeCheckItems["structural-upgrade"],
                        TypeCheckAppearance(
                            "structural-upgrade",
                            hasCore: true,
                            coreRadiusFraction: 0.2f,
                            textureOffset: 30
                        ),
                        PipeBehavior.Structure,
                        StructuralCheckFamily,
                        geometry: new PipeGeometryProfile(
                            radius: 0.4f,
                            centerHeight: 0.8f,
                            fittingScale: 1.15f,
                            supportRingWidth: 0.055f
                        )
                    )
                );
                PipeTypes.Register(
                    TypeCheckOwner,
                    new PipeTypeDefinition(
                        "other-structure",
                        typeCheckItems["other-structure"],
                        TypeCheckAppearance("other-structure", failConfiguration: true),
                        behavior: PipeBehavior.Structure
                    )
                );
                PipeTypes.Register(
                    TypeCheckOwner,
                    new PipeTypeDefinition(
                        "structural-interior",
                        typeCheckItems["structural-interior"],
                        TypeCheckAppearance("structural-interior", interior: TypeCheckInterior()),
                        PipeBehavior.Structure,
                        StructuralCheckFamily,
                        directionMode: PipeDirectionMode.Directed,
                        geometry: new PipeGeometryProfile(radius: 0.8f, centerHeight: 0.75f)
                    )
                );
                PipeTypes.Register(
                    TypeCheckOwner,
                    new PipeTypeDefinition(
                        "directed-interior",
                        typeCheckItems["directed-interior"],
                        TypeCheckAppearance(
                            "directed-interior",
                            interior: TypeCheckAsymmetricInterior()
                        ),
                        PipeBehavior.Structure,
                        StructuralCheckFamily,
                        directionMode: PipeDirectionMode.Directed,
                        geometry: new PipeGeometryProfile(radius: 0.8f, centerHeight: 0.75f)
                    )
                );
                PipeTypes.Register(
                    TypeCheckOwner,
                    new PipeTypeDefinition(
                        "open-beam",
                        typeCheckItems["open-beam"],
                        TypeCheckAppearance(
                            "open-beam",
                            interior: TypeCheckOpenInterior(),
                            wallMode: PipeWallMode.Open
                        ),
                        PipeBehavior.Structure,
                        StructuralCheckFamily,
                        geometry: new PipeGeometryProfile(radius: 0.8f, centerHeight: 0.75f)
                    )
                );
                var duplicateKeyDraft = PipeTypes.CreateItem(
                    nextItemId,
                    "FluidPipes.Checks.duplicate-key",
                    "FluidPipes.Description"
                );
                ExpectTypeException<InvalidOperationException>(
                    () =>
                        PipeTypes.Register(
                            TypeCheckOwner,
                            new PipeTypeDefinition("shared-variant", duplicateKeyDraft)
                        ),
                    "registration rejects duplicate owner-qualified type keys"
                );
                ExpectTypeException<InvalidOperationException>(
                    () =>
                        PipeTypes.Register(
                            TypeCheckOwner,
                            new PipeTypeDefinition(
                                "duplicate-item",
                                typeCheckItems["shared-variant"]
                            )
                        ),
                    "registration rejects duplicate item bindings"
                );
                Check(
                    !PipeTypes.IsPipeItem(duplicateKeyDraft.ID)
                        && !PipeTypes.TryGet(TypeCheckOwner + ":duplicate-item", out _),
                    "rejected registration leaves both lookup indexes unchanged"
                );

                Exception backgroundError = null;
                var backgroundRegistration = new Thread(() =>
                {
                    try
                    {
                        PipeTypes.Register(
                            TypeCheckOwner,
                            new PipeTypeDefinition("worker-thread", duplicateKeyDraft)
                        );
                    }
                    catch (Exception error)
                    {
                        backgroundError = error;
                    }
                });
                backgroundRegistration.Start();
                backgroundRegistration.Join();
                Check(
                    backgroundError is InvalidOperationException
                        && !PipeTypes.TryGet(TypeCheckOwner + ":worker-thread", out _),
                    "registration requires the Unity main thread"
                );
                Check(
                    !PipeApi.IsReady
                        && PipeTypes.TryGet(TypeCheckOwner + ":shared-variant", out var variant)
                        && variant.ItemId == typeCheckItems["shared-variant"].ID,
                    "pre-add registration exposes type identity before factory operations are ready"
                );
                typePreAddCompleted = true;
            }
        );
    }

    private void CompletePipeTypeChecks()
    {
        Test(
            "pipe type post-add registration",
            () =>
            {
                var track = typeCheckItems["directed-track"];
                Check(
                    LDB.items.Select(track.ID) == track && track.prefabDesc.isBelt,
                    "post-add type registration binds an initialized native belt item"
                );
                PipeTypes.Register(
                    TypeCheckOwner,
                    new PipeTypeDefinition(
                        "directed-track",
                        track,
                        TypeCheckAppearance("directed-track", hasCore: true),
                        PipeBehavior.Structure,
                        StructuralCheckFamily,
                        batchGroup: (factory, entityId) => entityId % 2,
                        directionMode: PipeDirectionMode.Directed
                    )
                );
                typePostAddCompleted = true;
            }
        );
    }

    private void PipeTypeRegistration()
    {
        Check(
            typePreAddCompleted && typePostAddCompleted && PipeApi.IsReady,
            "both LDB registration stages finish before pipe readiness"
        );
        foreach (var registeredItem in typeCheckItems)
        {
            Check(
                PipeTypes.TryGet(registeredItem.Value.ID, out var byItem)
                    && PipeTypes.TryGet(TypeCheckOwner + ":" + registeredItem.Key, out var byKey)
                    && byItem.Key == byKey.Key
                    && byItem.ItemId == registeredItem.Value.ID
                    && byItem.OwnerGuid == TypeCheckOwner
                    && LDB.items.Select(byItem.ItemId) == registeredItem.Value,
                "sealed type lookups resolve the initialized item " + registeredItem.Key
            );
        }

        var nativePipe = LDB.items.Select(PipeApi.PipeItemId);
        var sharedVariant = typeCheckItems["shared-variant"];
        Check(
            sharedVariant != nativePipe
                && sharedVariant.Name == "FluidPipes.Checks.shared-variant"
                && sharedVariant.BuildIndex == 0
                && sharedVariant.ModelIndex == nativePipe.ModelIndex
                && !ReferenceEquals(sharedVariant.Upgrades, nativePipe.Upgrades),
            "item drafts retain independent identity and upgrade arrays with native belt geometry"
        );
        Check(
            PipeTypes.TryGet(PipeApi.PipeItemId, out var defaultType)
                && PipeTypes.TryGet(sharedVariant.ID, out var variantType)
                && defaultType.ConnectionFamily == variantType.ConnectionFamily
                && variantType.Behavior == PipeBehavior.SharedInventory
                && variantType.DirectionMode == PipeDirectionMode.Undirected,
            "default shared variants inherit the fluid connection family"
        );
        ExpectTypeException<InvalidOperationException>(
            () =>
                PipeTypes.Register(
                    TypeCheckOwner,
                    new PipeTypeDefinition("late-registration", sharedVariant)
                ),
            "registration closes after the prototype database becomes ready"
        );
        ExpectTypeException<InvalidOperationException>(
            () => PipeTypes.CreateItem(11999, "FluidPipes.Checks.late", "FluidPipes.Description"),
            "item creation closes with type registration"
        );
        ExpectTypeException<ArgumentException>(
            () =>
                new PipeTypeDefinition(
                    "directed-inventory",
                    sharedVariant,
                    directionMode: PipeDirectionMode.Directed
                ),
            "a shared inventory requires symmetric connection semantics"
        );
        foreach (
            float invalidRadius in new[] { float.NaN, float.PositiveInfinity, 0, -0.1f, 0.86f }
        )
        {
            ExpectTypeException<ArgumentOutOfRangeException>(
                () => new PipeAppearance(coreRadiusFraction: invalidRadius),
                "appearance validates conductor radius " + invalidRadius
            );
        }
        PipeGeometryProfileArguments();
        foreach (
            float invalidPeriod in new[]
            {
                float.NaN,
                float.PositiveInfinity,
                float.NegativeInfinity,
                -1,
            }
        )
        {
            ExpectTypeException<ArgumentOutOfRangeException>(
                () => new PipeAppearance(textureCoordinatePeriod: invalidPeriod),
                "appearance rejects an invalid longitudinal coordinate period " + invalidPeriod
            );
        }
        Check(
            new PipeAppearance().TextureCoordinatePeriod == 0
                && new PipeAppearance(textureCoordinatePeriod: 1).TextureCoordinatePeriod == 1,
            "appearance supports explicit periodic coordinates and defaults to nonperiodic mapping"
        );

        Check(
            !PipeTypes.IsPipeItem(2001)
                && !PipeTypes.TryGet("unregistered:pipe", out _)
                && PipeTypes.GetNeighbors(null, 1).Length == 0
                && PipeTypes.GetConnectedNodes(null, 1).Length == 0
                && PipeTypes.GetIncomingNeighbors(null, 1).Length == 0
                && PipeTypes.GetOutgoingNeighbors(null, 1).Length == 0,
            "type queries handle ordinary belts and unavailable entity targets"
        );
    }

    private void PipeTypeInventory()
    {
        WithTypeNativePaths(PipeTypeInventoryCore);
    }

    private void PipeTypeInventoryCore()
    {
        PipeRuntime.Clear();
        var factory = Fixture(7, 981001, false);
        SetTypeCheckPrototype(factory, 2, "shared-variant");
        SetTypeCheckPrototype(factory, 3, "isolated-inventory");
        SetTypeCheckPrototype(factory, 4, "structural-frame");
        SetTypeCheckPrototype(factory, 5, "structural-frame");
        SetTypeCheckPrototype(factory, 6, "other-structure");
        factory.entityPool[7].protoId = 2001;
        RebuildTypeNativePaths(factory, 6);

        factory.WriteObjectConn(1, 0, true, 2, 1);
        Check(
            PipeApi.IsPipe(factory, 2)
                && PipeApi.ShareInventory(factory, 1, 2)
                && PipeApi.TryInsert(factory, 1, 1000, 18, 54)
                && PipeApi.TryGetInventory(factory, 2, out var inventory)
                && inventory.Count == 18
                && inventory.Capacity == 20
                && inventory.ProliferatorPoints == 54,
            "different item variants in the fluid family share native connections and inventory"
        );
        Check(
            factory.InsertInto(2, 0, 1000, 1, 3, out _) == 1
                && PipeApi.TryTake(factory, 1, 19, out var extracted)
                && extracted.Count == 19
                && extracted.ProliferatorPoints == 57,
            "native insertion through a variant conserves remote extraction and points"
        );
        foreach (int incompatibleEntity in new[] { 3, 4, 7 })
        {
            factory.WriteObjectConn(2, 0, true, incompatibleEntity, 1);
            factory.ReadObjectConn(2, 0, out _, out int destination, out _);
            Check(
                destination == 0
                    && !PipeTypes.GetNeighbors(factory, 2).Contains(incompatibleEntity),
                "native writes reject incompatible families or ordinary belts at entity "
                    + incompatibleEntity
            );
        }

        factory.WriteObjectConn(4, 0, true, 5, 1);
        factory.WriteObjectConn(5, 0, true, 6, 1);
        Check(
            PipeApi.IsPipe(factory, 4)
                && PipeTypes
                    .GetConnectedNodes(factory, 4)
                    .OrderBy(entityId => entityId)
                    .SequenceEqual(new[] { 4, 5 })
                && PipeTypes.GetIncomingNeighbors(factory, 4).SequenceEqual(new[] { 5 })
                && PipeTypes.GetOutgoingNeighbors(factory, 4).SequenceEqual(new[] { 5 }),
            "structural families expose symmetric topology and remain isolated"
        );
        Check(
            !PipeApi.TryGetInventory(factory, 4, out _)
                && !PipeApi.TryGetNetwork(factory, 4, out _)
                && !PipeApi.TryInsert(factory, 4, 1000, 1)
                && !PipeApi.TryTake(factory, 5, 1, out _)
                && factory.InsertInto(4, 0, 1000, 1, 0, out _) == 0
                && factory.cargoTraffic.TryPickItem(5, 0, 1000, out _, out _) == 0,
            "externally simulated structures reject public and native inventory operations"
        );
        var detachedNeighbors = PipeTypes.GetNeighbors(factory, 4);
        var detachedComponent = PipeTypes.GetConnectedNodes(factory, 4);
        detachedNeighbors[0] = int.MaxValue;
        detachedComponent[0] = int.MaxValue;
        Check(
            PipeTypes.GetNeighbors(factory, 4).SequenceEqual(new[] { 5 })
                && !PipeTypes.GetConnectedNodes(factory, 4).Contains(int.MaxValue),
            "topology queries return detached snapshots"
        );
        Check(
            PipeApi.TryInsert(factory, 1, 1000, 16, 48)
                && PipeConnections.Disconnect(factory, 1, 2)
                && PipeApi.TryGetInventory(factory, 1, out var firstSpace)
                && PipeApi.TryGetInventory(factory, 2, out var secondSpace)
                && firstSpace.Count == 8
                && secondSpace.Count == 8
                && firstSpace.ProliferatorPoints + secondSpace.ProliferatorPoints == 48,
            "cutting a mixed-appearance shared network partitions inventory by node capacity"
        );

        RoundTripTypeState();
        Check(
            PipeApi.TryGetInventory(factory, 2, out var restored)
                && restored.Count == 8
                && restored.ProliferatorPoints == 24
                && PipeTypes.GetNeighbors(factory, 1).Length == 0
                && PipeTypes.GetConnectedNodes(factory, 4).Length == 2,
            "typed inventories and structural components survive save import and factory rebinding"
        );
        PipeRuntime.Clear();
    }

    private void DirectedPipeTypeTopology()
    {
        WithTypeNativePaths(DirectedPipeTypeTopologyCore);
    }

    private void DirectedPipeTypeTopologyCore()
    {
        PipeRuntime.Clear();
        var factory = Fixture(5, 981002, false);
        for (int entityId = 1; entityId <= 4; entityId++)
        {
            SetTypeCheckPrototype(factory, entityId, "directed-track");
        }
        SetTypeCheckPrototype(factory, 5, "structural-frame");

        factory.WriteObjectConn(1, 0, true, 2, 1);
        factory.WriteObjectConn(2, 0, true, 3, 1);
        factory.WriteObjectConn(4, 0, true, 2, 2);
        RebuildTypeNativePaths(factory, 4);
        Check(
            PipeTypes.GetOutgoingNeighbors(factory, 2).SequenceEqual(new[] { 3 })
                && PipeTypes
                    .GetIncomingNeighbors(factory, 2)
                    .OrderBy(entityId => entityId)
                    .SequenceEqual(new[] { 1, 4 })
                && PipeTypes
                    .GetConnectedNodes(factory, 3)
                    .OrderBy(entityId => entityId)
                    .SequenceEqual(new[] { 1, 2, 3, 4 }),
            "directed structures retain native input/output orientation within a weak component"
        );
        factory.WriteObjectConn(3, 0, true, 5, 1);
        factory.ReadObjectConn(3, 0, out _, out int incompatibleTarget, out _);
        Check(
            incompatibleTarget == 0
                && PipeTypes.GetNeighbors(factory, 5).Length == 0
                && !PipeApi.TryInsert(factory, 2, 1000, 1),
            "direction policies remain isolated inside an equal family key"
        );
        Check(
            PipeConnections.Disconnect(factory, 2, 3)
                && factory.cargoTraffic.beltPool[2].outputId == 0
                && PipeTypes.GetOutgoingNeighbors(factory, 2).Length == 0
                && PipeTypes.GetIncomingNeighbors(factory, 3).Length == 0
                && PipeTypes.GetConnectedNodes(factory, 3).SequenceEqual(new[] { 3 })
                && PipeTypes.GetConnectedNodes(factory, 1).Length == 3
                && !PipeConnections.Disconnect(factory, 3, 2),
            "directed edge removal clears native orientation and splits the weak component"
        );
        RoundTripTypeState();
        Check(
            PipeTypes.GetOutgoingNeighbors(factory, 1).SequenceEqual(new[] { 2 })
                && PipeTypes
                    .GetIncomingNeighbors(factory, 2)
                    .OrderBy(entityId => entityId)
                    .SequenceEqual(new[] { 1, 4 })
                && PipeTypes.GetConnectedNodes(factory, 1).Length == 3
                && PipeTypes.GetConnectedNodes(factory, 3).Length == 1,
            "directed orientation and a removed bridge survive save import and rebinding"
        );
        factory.WriteObjectConn(3, 0, true, 2, 3);
        Check(
            PipeTypes.GetOutgoingNeighbors(factory, 3).SequenceEqual(new[] { 2 })
                && PipeTypes.GetIncomingNeighbors(factory, 2).Length == 3
                && PipeTypes.GetConnectedNodes(factory, 1).Length == 4,
            "a restored structural network accepts a reversed native edge"
        );

        var ringFactory = Fixture(4, 981003, false);
        for (int entityId = 1; entityId <= 4; entityId++)
        {
            SetTypeCheckPrototype(ringFactory, entityId, "directed-track");
        }
        for (int entityId = 1; entityId <= 4; entityId++)
        {
            ringFactory.WriteObjectConn(entityId, 0, true, entityId % 4 + 1, 1);
        }
        RebuildTypeNativePaths(ringFactory, 4);
        var ringTraffic = ringFactory.cargoTraffic;
        Check(
            ringTraffic.GetCargoPath(ringTraffic.beltPool[1].segPathId).closed
                && PipeTypes.GetOutgoingNeighbors(ringFactory, 4).SequenceEqual(new[] { 1 })
                && PipeTypes.GetConnectedNodes(ringFactory, 1).Length == 4,
            "directed native cycles retain CargoPath closure for external simulation"
        );
        Check(
            PipeConnections.Disconnect(ringFactory, 4, 1)
                && !ringTraffic.GetCargoPath(ringTraffic.beltPool[1].segPathId).closed
                && PipeTypes.GetOutgoingNeighbors(ringFactory, 4).Length == 0
                && PipeTypes.GetConnectedNodes(ringFactory, 1).Length == 4,
            "cutting a directed cycle opens its native path and retains its weak component"
        );
        PipeRuntime.Clear();
    }

    private void PipeTypeNotifications()
    {
        WithTypeNativePaths(() =>
        {
            PipeRuntime.Clear();
            var factory = Fixture(3, 981004, false);
            for (int entityId = 1; entityId <= 3; entityId++)
            {
                SetTypeCheckPrototype(factory, entityId, "directed-track");
            }

            var observedEntities = new List<int>();
            bool observedFinalState = true;
            bool observedOnMainThread = true;
            int mainThreadId = Thread.CurrentThread.ManagedThreadId;
            int expectedComponentSize = 3;
            Action<PlanetFactory, int> observer = (changedFactory, changedEntity) =>
            {
                if (changedFactory != factory)
                {
                    return;
                }

                observedEntities.Add(changedEntity);
                observedOnMainThread &= Thread.CurrentThread.ManagedThreadId == mainThreadId;
                if (PipeApi.IsPipe(changedFactory, changedEntity))
                {
                    observedFinalState &=
                        PipeTypes.GetConnectedNodes(changedFactory, changedEntity).Length
                        == expectedComponentSize;
                }
            };
            PipeTypes.FlushChanges();
            PipeTypes.ConnectionsChanged += observer;
            try
            {
                factory.WriteObjectConn(1, 0, true, 2, 1);
                factory.WriteObjectConn(2, 0, true, 3, 1);
                RebuildTypeNativePaths(factory, 3);
                Check(
                    observedEntities.Count == 0,
                    "connection observers wait for the completed mutation batch"
                );
                PipeTypes.FlushChanges();
                Check(
                    observedFinalState
                        && observedEntities
                            .OrderBy(entityId => entityId)
                            .SequenceEqual(new[] { 1, 2, 3 }),
                    "each changed node is reported once with completed component queries"
                );
                observedEntities.Clear();
                expectedComponentSize = 1;
                factory.cargoTraffic.RemoveBeltComponent(2);
                PipeTypes.FlushChanges();
                Check(
                    observedFinalState
                        && observedEntities.Contains(1)
                        && observedEntities.Contains(3)
                        && !PipeApi.IsPipe(factory, 2)
                        && PipeTypes.GetConnectedNodes(factory, 1).Length == 1
                        && PipeTypes.GetConnectedNodes(factory, 3).Length == 1,
                    "native dismantling reports surviving neighbors after component separation"
                );
                observedEntities.Clear();
                var workers = new[]
                {
                    new Thread(() =>
                    {
                        for (int mutation = 0; mutation < 100; mutation++)
                        {
                            PipeTypes.Changed(factory, 1);
                        }
                    }),
                    new Thread(() =>
                    {
                        for (int mutation = 0; mutation < 100; mutation++)
                        {
                            PipeTypes.Changed(factory, 3);
                        }
                    }),
                };
                foreach (var worker in workers)
                {
                    worker.Start();
                }
                foreach (var worker in workers)
                {
                    worker.Join();
                }
                Check(
                    observedEntities.Count == 0,
                    "worker notifications remain queued until the main-thread dispatch"
                );
                PipeTypes.FlushChanges();
                Check(
                    observedOnMainThread
                        && observedEntities
                            .OrderBy(entityId => entityId)
                            .SequenceEqual(new[] { 1, 3 }),
                    "concurrent notification producers deliver one callback per node on the main thread"
                );
            }
            finally
            {
                PipeTypes.ConnectionsChanged -= observer;
                PipeRuntime.Clear();
            }
        });
    }

    private static void RebuildTypeNativePaths(PlanetFactory factory, int nodeCount)
    {
        var traffic = factory.cargoTraffic;
        for (int entityId = 1; entityId <= nodeCount; entityId++)
        {
            traffic.AlterBeltConnections(factory.entityPool[entityId].beltId, 0, 0, 0, 0);
        }
        for (int entityId = 1; entityId <= nodeCount; entityId++)
        {
            PipeConnections.NativeLinks(
                traffic,
                entityId,
                out int output,
                out int firstInput,
                out int secondInput,
                out int thirdInput
            );
            traffic.AlterBeltConnections(
                factory.entityPool[entityId].beltId,
                output,
                firstInput,
                secondInput,
                thirdInput
            );
        }
    }

    private static void WithTypeNativePaths(Action test)
    {
        var harmony = new Harmony(TypeCheckOwner + ".type-paths");
        bool previousSyntheticRendering = syntheticRendering;
        foreach (
            string methodName in new[]
            {
                "AlterBeltRenderer",
                "AlterPathRenderer",
                "RemoveBeltRenderer",
                "RemovePathRenderer",
                "RefreshPathUV",
            }
        )
        {
            harmony.Patch(
                AccessTools.Method(typeof(CargoTraffic), methodName),
                prefix: new HarmonyMethod(typeof(RuntimeChecks), nameof(RenderPrefix))
            );
        }
        syntheticRendering = true;
        try
        {
            test();
        }
        finally
        {
            syntheticRendering = previousSyntheticRendering;
            harmony.UnpatchSelf();
        }
    }

    private void SetTypeCheckPrototype(PlanetFactory factory, int entityId, string localName)
    {
        var prototype = typeCheckItems[localName];
        factory.entityPool[entityId].protoId = (short)prototype.ID;
        factory.entityPool[entityId].modelIndex = (short)prototype.ModelIndex;
        PipeRuntime.Sync(factory.cargoTraffic, factory.entityPool[entityId].beltId);
    }

    private static void RoundTripTypeState()
    {
        using (var stream = new MemoryStream())
        {
            PipeRuntime.Export(new BinaryWriter(stream));
            stream.Position = 0;
            PipeRuntime.Import(new BinaryReader(stream));
        }
    }

    private void ExpectTypeException<TException>(Action operation, string description)
        where TException : Exception
    {
        bool rejected = false;
        try
        {
            operation();
        }
        catch (TException)
        {
            rejected = true;
        }
        Check(rejected, description);
    }
}
