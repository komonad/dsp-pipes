using System;
using System.Linq;
using FluidPipes;
using FluidPipes.Integration.Belts;

public sealed partial class RuntimeChecks
{
    private void BlueprintGraphParameters()
    {
        const int marker = 0x46504731;
        var building = new BlueprintBuilding
        {
            index = 0,
            itemId = (short)Prototypes.ItemId,
            parameters = new[] { 42, marker, 2, 1, 2, 1, marker },
        };
        var peers = PipeBlueprints.Neighbors(building, 3, out int nativeCount);
        Check(
            peers.SequenceEqual(new[] { 1, 2 }) && nativeCount == 1 && building.parameters[0] == 42,
            "blueprint graph trailer retains native parameters and explicit neighbor indices"
        );
        building.itemId = 2001;
        Check(
            PipeBlueprints.Neighbors(building, 3, out nativeCount) == null
                && nativeCount == building.parameters.Length,
            "ordinary belt parameters remain outside the pipe blueprint format"
        );
        building.itemId = (short)Prototypes.ItemId;
        foreach (
            var invalid in new[]
            {
                new[] { marker },
                new[] { marker, 1, 1, -1, marker },
                new[] { marker, 1, 1, int.MaxValue, marker },
                new[] { marker, 2, 1, 0, marker },
                new[] { marker, 1, 0, 0, marker },
                new[] { marker, 2, 2, 1, 0, marker },
                new[] { marker, 2, 1, 1, 0, marker },
                new[] { marker, 1, 3, 0, marker },
            }
        )
        {
            building.parameters = invalid;
            ExpectTypeException<FormatException>(
                () => PipeBlueprints.Neighbors(building, 3, out _),
                "malformed blueprint graph lengths, self edges, duplicates and out-of-range indices are rejected"
            );
        }
        building.parameters = new int[0];
        Check(
            PipeBlueprints.Neighbors(building, 3, out nativeCount) == null && nativeCount == 0,
            "pipe blueprints without graph metadata retain native connection semantics"
        );
        BlueprintGraphRejectsMixedInventory();
    }

    private void BlueprintGraphRejectsMixedInventory()
    {
        const int marker = 0x46504731;
        var factory = Fixture(2, 981112, false);
        var graph = PipeRuntime.Bind(factory.cargoTraffic);
        Check(
            graph.Insert(1, 1000, 10, 4) && graph.Insert(2, 1007, 7, 2),
            "blueprint merge rejection fixture holds two independent fluid inventories"
        );
        var blueprint = BlueprintData.CreateNew();
        blueprint.buildings = new[]
        {
            new BlueprintBuilding
            {
                index = 0,
                itemId = (short)Prototypes.ItemId,
                parameters = new[] { marker, 1, 1, 0, marker },
            },
            new BlueprintBuilding
            {
                index = 1,
                itemId = (short)Prototypes.ItemId,
                parameters = new[] { marker, 0, 0, marker },
            },
        };
        var item = LDB.items.Select(Prototypes.ItemId);
        var tool = new BuildTool_BlueprintPaste
        {
            factory = factory,
            blueprint = blueprint,
            bpCursor = 2,
            bpPool = new[]
            {
                new BuildPreview
                {
                    item = item,
                    coverObjId = 1,
                    objId = 1,
                    condition = EBuildCondition.Ok,
                },
                new BuildPreview
                {
                    item = item,
                    coverObjId = 2,
                    objId = 2,
                    condition = EBuildCondition.Ok,
                },
            },
        };
        int prebuildCursor = factory.prebuildCursor;
        tool.CreatePrebuilds();
        Check(
            tool.bpPool.All(preview => preview.condition == EBuildCondition.Failure)
                && factory.prebuildCursor == prebuildCursor
                && graph.View(1).Count == 10
                && graph.View(1).ProliferatorPoints == 4
                && graph.View(2).Count == 7
                && graph.View(2).ProliferatorPoints == 2
                && graph.Links(1).Length == 0
                && graph.Links(2).Length == 0,
            "forced blueprint placement rejects mixing before any entity, graph or stock mutation"
        );
    }
}
