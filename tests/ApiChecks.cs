using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using FluidPipes;
using FluidPipes.API;

public sealed partial class RuntimeChecks
{
    private void ApiIntegration()
    {
        var factory = Fixture(3, 980001, true);
        var traffic = factory.cargoTraffic;
        var graph = PipeRuntime.Bind(traffic);
        NetworkInventory inventory = default(NetworkInventory);
        Check(
            PipeApi.IsReady
                && PipeApi.PipeItemId == Prototypes.ItemId
                && PipeApi.ValveItemId == Prototypes.ValveId,
            "API exposes initialized prototype IDs"
        );
        ExpectTypeException<InvalidOperationException>(
            PipeApi.DisableBuiltInItems,
            "initialized default content remains fixed when a late opt-out call is rejected"
        );
        ExpectTypeException<InvalidOperationException>(
            () =>
                PipeTypes.SetItemIcon(
                    LDB.items.Select(PipeApi.PipeItemId),
                    LDB.items.Select(PipeApi.PipeItemId).iconSprite
                ),
            "item icon assignment closes after prototype initialization"
        );
        Check(
            !PipeApi.IsPipe(null, 1)
                && !PipeApi.IsPipe(factory, -1)
                && !PipeApi.IsPipe(factory, int.MaxValue)
                && !PipeApi.TryGetInventory(factory, 0, out _)
                && !PipeApi.TryInsert(factory, 0, 1000, 1),
            "API rejects invalid entity targets"
        );
        Check(
            PipeApi.IsItemAllowed(1000)
                && !PipeApi.IsItemAllowed(1101)
                && !PipeApi.IsItemAllowed(int.MaxValue),
            "default policy admits registered fluids"
        );
        Check(
            PipeApi.TryInsert(factory, 1, 1000, 10, 40)
                && PipeApi.TryGetInventory(factory, 3, out inventory)
                && inventory.Count == 10
                && inventory.ProliferatorPoints == 40
                && inventory.Capacity == 30,
            "API inventory includes conserved points"
        );
        Check(
            !PipeApi.TryInsert(factory, 1, 1000, 21)
                && !PipeApi.TryInsert(factory, 1, 1000, 1, 11)
                && !PipeApi.TryTake(factory, 3, 1, out _, 1007),
            "API rejects overflow, invalid points and mismatched filters"
        );
        Check(
            PipeApi.TryTake(factory, 3, 100, out var amount)
                && amount.Count == 10
                && amount.ProliferatorPoints == 40
                && inventory.Count == 10
                && PipeApi.ShareInventory(factory, 1, 3),
            "API extracts remotely and returns detached snapshots"
        );

        var firstGrant = PipeApi.RegisterTransportItems(1101);
        var secondGrant = PipeApi.RegisterTransportItems(1101);
        try
        {
            firstGrant.Dispose();
            Check(
                PipeApi.IsItemAllowed(1101),
                "overlapping mod registrations retain independent ownership"
            );
            Check(
                factory.InsertInto(1, 0, 1101, 4, 16, out _) == 4
                    && PipeApi.TryTake(factory, 3, 2, out amount, 1101)
                    && amount.Count == 2
                    && amount.ProliferatorPoints == 8,
                "configured solids use native factory insertion and API extraction"
            );
            Check(
                traffic.TryPickItem(3, 0, 1101, out byte stack, out byte points) == 1101
                    && stack == 1
                    && points == 4,
                "configured solids use native cargo extraction"
            );
            secondGrant.Dispose();
            Check(
                !PipeApi.IsItemAllowed(1101)
                    && !PipeApi.TryInsert(factory, 1, 1101, 1)
                    && PipeApi.TryTake(factory, 3, 10, out amount)
                    && amount.Count == 1
                    && amount.ProliferatorPoints == 4,
                "revoked admission preserves and permits extraction of stored items"
            );

            using (PipeApi.RegisterTransportItems(1101))
            {
                TransportItemRegistry.Configure(false, new[] { 1101, 1000 }, new[] { 1101 });
                Check(
                    PipeApi.IsItemAllowed(1000)
                        && !PipeApi.IsItemAllowed(1007)
                        && !PipeApi.IsItemAllowed(1101),
                    "explicit configuration exclusions override defaults and mod grants"
                );
            }
        }
        finally
        {
            firstGrant.Dispose();
            secondGrant.Dispose();
            TransportItemRegistry.Configure(true, new int[0], new int[0]);
        }

        using (PipeApi.RegisterTransportItems(1101))
        {
            Check(
                PipeApi.TryInsert(factory, 1, 1101, 28, 112),
                "configured solid fills a pure pipe space"
            );
            int remainingItems = 5;
            int remainingPoints = 20;
            traffic.TryInsertItemToBeltWithStackIncreasement(
                1,
                0,
                1101,
                4,
                ref remainingItems,
                ref remainingPoints
            );
            Check(
                remainingItems == 3
                    && remainingPoints == 12
                    && PipeApi.TryTake(factory, 3, 300, out amount)
                    && amount.Count == 30
                    && amount.ProliferatorPoints == 120,
                "native partial insertion preserves unaccepted solid items and points"
            );
            var station = new StationComponent
            {
                storage = new[]
                {
                    new StationStore { itemId = 1101, max = 1000 },
                },
                slots = new[]
                {
                    new SlotData { beltId = 1, dir = IODir.Input },
                },
                needs = new int[6],
            };
            AttachStation(factory, station);
            Check(
                PipeApi.TryInsert(factory, 1, 1101, 500, 2000),
                "solid insertion binds an unselected matching tower slot"
            );
            PipePools.Tick(factory);
            Check(
                PipeApi.TryGetNetwork(factory, 3, out var snapshot)
                    && snapshot.Inventory.Count == 500
                    && snapshot.Inventory.Capacity == 1030
                    && snapshot.PipeNodeCount == 3
                    && snapshot.ContainerCount == 1
                    && station.storage[0].count > 0,
                "solid tower storage participates in shared capacity"
            );
            int requested = 200;
            int item = 1101;
            station.TakeItem(ref item, ref requested, out int proliferatorPoints);
            Check(
                item == 1101
                    && requested == 200
                    && proliferatorPoints == 800
                    && PipePools.View(graph, 1, true).Count == 300,
                "native tower extraction draws solid items from the shared space"
            );
            PipePools.Empty(graph, 1);

            var tankFactory = Fixture(1, 980002, true);
            tankFactory.factoryStorage = new FactoryStorage(tankFactory.planet);
            tankFactory.factoryStorage.tankCursor = 2;
            tankFactory.factoryStorage.tankPool[1] = new TankComponent
            {
                id = 1,
                entityId = 2,
                belt0 = 1,
                fluidCapacity = 10000,
            };
            Check(
                PipeApi.TryGetInventory(tankFactory, 1, out inventory)
                    && inventory.RequiresFluid
                    && !PipeApi.TryInsert(tankFactory, 1, 1101, 10)
                    && tankFactory.factoryStorage.tankPool[1].fluidCount == 0,
                "native tanks retain fluid-only storage with solid transport enabled"
            );
            Check(
                PipeApi.TryInsert(tankFactory, 1, 1000, 10),
                "native tanks still accept admitted fluids"
            );
            Check(
                PipeApi.TryInsert(factory, 1, 1101, 10),
                "solid network refills after shared extraction"
            );
            factory.factoryStorage = new FactoryStorage(factory.planet);
            factory.factoryStorage.tankCursor = 2;
            factory.factoryStorage.tankPool[1] = new TankComponent
            {
                id = 1,
                entityId = 4,
                fluidCapacity = 10000,
            };
            AddObject(factory, 4, 2106);
            factory.entityPool[4].tankId = 1;
            Check(
                !PipePools.CanAttach(factory, 1, 4, 0)
                    && factory.factoryStorage.tankPool[1].fluidCount == 0,
                "tank attachment rejects an existing solid storage space"
            );
        }

        var valveFactory = Fixture(2, 980003, true);
        AddObject(valveFactory, 3, Prototypes.ValveId);
        var options = new ValveOptions(
            itemsPerSecond: 240,
            targetFraction: 0.9,
            direction: FlowDirection.AtoB,
            control: LevelControl.KeepB
        );
        Check(
            PipeApi.TrySetValveOptions(valveFactory, 3, options)
                && PipeApi.TryGetValveOptions(valveFactory, 3, out var readBack)
                && readBack.ItemsPerSecond == 240
                && readBack.TargetFraction == 0.9
                && readBack.Direction == FlowDirection.AtoB
                && readBack.Control == LevelControl.KeepB
                && !PipeApi.TrySetValveOptions(valveFactory, 1, options),
            "API valve settings round trip without mutable shared state"
        );
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, -1, 1000001 })
        {
            bool rejected = false;
            try
            {
                new ValveOptions(itemsPerSecond: invalid);
            }
            catch (ArgumentOutOfRangeException)
            {
                rejected = true;
            }
            Check(rejected, "API rejects invalid valve rate " + invalid);
        }
    }

    private void ApiDocumentation()
    {
        var assembly = typeof(PipeApi).Assembly;
        Check(
            assembly
                .GetExportedTypes()
                .All(type => type.Namespace == "FluidPipes.API" || type == typeof(Plugin)),
            "implementation types remain outside the exported plugin contract"
        );
        var document = new XmlDocument();
        document.Load(Path.ChangeExtension(assembly.Location, ".xml"));
        const BindingFlags declaredPublic =
            BindingFlags.Public
            | BindingFlags.Instance
            | BindingFlags.Static
            | BindingFlags.DeclaredOnly;
        foreach (
            var type in assembly
                .GetExportedTypes()
                .Where(type => type.Namespace == "FluidPipes.API")
        )
        {
            RequireDocumentation(document, "T:" + type.FullName);
            foreach (var member in type.GetMembers(declaredPublic))
            {
                if (
                    member is MethodInfo accessor && accessor.IsSpecialName
                    || member is FieldInfo specialField && specialField.IsSpecialName
                )
                {
                    continue;
                }
                if (member is MethodBase method)
                {
                    var parameters = method.GetParameters();
                    string signature =
                        parameters.Length == 0
                            ? ""
                            : "("
                                + string.Join(
                                    ",",
                                    parameters.Select(parameter =>
                                        DocumentationTypeName(parameter.ParameterType)
                                    )
                                )
                                + ")";
                    var entry = RequireDocumentation(
                        document,
                        "M:"
                            + type.FullName
                            + "."
                            + (method.IsConstructor ? "#ctor" : method.Name)
                            + signature
                    );
                    foreach (var parameter in parameters)
                    {
                        if (entry.SelectSingleNode("param[@name='" + parameter.Name + "']") == null)
                        {
                            throw new Exception(
                                "Missing parameter documentation: "
                                    + member.Name
                                    + "."
                                    + parameter.Name
                            );
                        }
                    }
                }
                else if (member is PropertyInfo || member is FieldInfo || member is EventInfo)
                {
                    string prefix =
                        member is PropertyInfo ? "P:"
                        : member is EventInfo ? "E:"
                        : "F:";
                    RequireDocumentation(document, prefix + type.FullName + "." + member.Name);
                }
            }
        }
        Check(
            true,
            "all supported public API types, members and parameters have XML documentation"
        );
    }

    private static string DocumentationTypeName(Type type)
    {
        if (type.IsByRef)
        {
            return DocumentationTypeName(type.GetElementType()) + "@";
        }

        if (type.IsPointer)
        {
            return DocumentationTypeName(type.GetElementType()) + "*";
        }

        if (type.IsArray)
        {
            string dimensions =
                type.GetArrayRank() == 1
                    ? ""
                    : string.Join(",", Enumerable.Repeat("0:", type.GetArrayRank()));
            return DocumentationTypeName(type.GetElementType()) + "[" + dimensions + "]";
        }

        if (type.IsGenericParameter)
        {
            string prefix = type.DeclaringMethod == null ? "`" : "``";
            return prefix + type.GenericParameterPosition;
        }

        if (type.IsGenericType)
        {
            string genericName = type.GetGenericTypeDefinition().FullName;
            int arityStart = genericName.IndexOf('`');
            return genericName.Substring(0, arityStart).Replace('+', '.')
                + "{"
                + string.Join(",", type.GetGenericArguments().Select(DocumentationTypeName))
                + "}";
        }

        return type.FullName.Replace('+', '.');
    }

    private static XmlNode RequireDocumentation(XmlDocument document, string memberId)
    {
        var node = document.SelectSingleNode("/doc/members/member[@name='" + memberId + "']");
        if (string.IsNullOrWhiteSpace(node?.SelectSingleNode("summary")?.InnerText))
        {
            throw new Exception("Missing API documentation: " + memberId);
        }
        return node;
    }
}
