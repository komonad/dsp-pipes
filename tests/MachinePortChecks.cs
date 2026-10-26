using System;
using System.Diagnostics;
using System.Linq;
using FluidPipes;
using FluidPipes.API;
using HarmonyLib;
using UnityEngine;

public sealed partial class RuntimeChecks
{
    private void MachinePortGeometry()
    {
        foreach (float outletHeight in new[] { 0.5f, 1.0f, -0.2f })
        {
            var builder = new PipeMesh();
            builder.MachinePort(Vector3.zero, Quaternion.identity, outletHeight);
            var mesh = builder.Build("machine-port-geometry");
            try
            {
                var positions = mesh.vertices;
                var directions = mesh.normals;
                Check(
                    mesh.GetTriangles(0).Length == 0
                        && mesh.GetTriangles(1).Length > 0
                        && mesh.subMeshCount == 4
                        && mesh.GetTriangles(2).Length > 0
                        && mesh.GetTriangles(3).Length > 0
                        && positions.All(position =>
                            !float.IsNaN(position.x) && !float.IsInfinity(position.y)
                        )
                        && directions.All(direction => direction.sqrMagnitude > 0.9f),
                    "Procedural machine casing has finite opaque geometry at outlet height "
                        + outletHeight
                );
                Check(
                    Mathf.Abs(mesh.bounds.max.z - PipeMesh.MachinePortDepth) < 0.00001f
                        && positions.Any(position =>
                            Mathf.Abs(position.z - PipeMesh.MachinePortDepth) < 0.00001f
                            && Mathf.Abs(position.y - outletHeight - PipeMesh.Radius) < 0.00001f
                        ),
                    "The procedural socket seals at the pipe centerline at outlet height "
                        + outletHeight
                );
                foreach (
                    float depth in new[] { 0.78f, 0.88f, 0.92f, 1.01f, PipeMesh.MachinePortDepth }
                )
                {
                    var ring = positions
                        .Where(position => Mathf.Abs(position.z - depth) < 0.00001f)
                        .Distinct()
                        .ToArray();
                    Check(
                        ring.Length >= 32
                            && Mathf.Abs(
                                ring.Min(position => position.x) + ring.Max(position => position.x)
                            ) < 0.00002f
                            && Mathf.Abs(
                                ring.Min(position => position.y)
                                    + ring.Max(position => position.y)
                                    - 2 * outletHeight
                            ) < 0.00002f
                            && ring.All(position =>
                                ring.Any(opposite =>
                                    Mathf.Abs(opposite.x + position.x) < 0.00001f
                                    && Mathf.Abs(opposite.y + position.y - 2 * outletHeight)
                                        < 0.00001f
                                )
                            ),
                        "Socket sections share the pipe axis at depth "
                            + depth
                            + " and height "
                            + outletHeight
                    );
                }
                var triangles = Enumerable
                    .Range(1, mesh.subMeshCount - 1)
                    .SelectMany(mesh.GetTriangles)
                    .ToArray();
                for (int index = 0; index < triangles.Length; index += 3)
                {
                    int first = triangles[index];
                    int second = triangles[index + 1];
                    int third = triangles[index + 2];
                    Vector3 face = Vector3.Cross(
                        positions[second] - positions[first],
                        positions[third] - positions[first]
                    );
                    if (
                        Vector3.Dot(
                            face,
                            directions[first] + directions[second] + directions[third]
                        ) < -0.00001f
                    )
                    {
                        throw new InvalidOperationException("Inverted machine-port surface.");
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }
    }

    private void MachinePortIntegration()
    {
        var enabledProperty = AccessTools.Property(typeof(PipeConfiguration), "EnableMachinePorts");
        bool previouslyEnabled = PipeConfiguration.EnableMachinePorts;
        enabledProperty.SetValue(null, true, null);
        try
        {
            const int nodeCount = 100000;
            const int machineEntityId = nodeCount + 1;
            var factory = Fixture(nodeCount, 980101, false);
            var graph = PipeRuntime.Bind(factory.cargoTraffic);
            const int destinationSlotOne = 1 << 24;
            const int inputConnection = 1 << 29;
            for (int entityId = 1; entityId < nodeCount; entityId++)
            {
                factory.entityConnPool[entityId * 16] = (entityId + 1) | destinationSlotOne;
                factory.entityConnPool[(entityId + 1) * 16 + 1] = entityId | inputConnection;
                PipeTopology.Connect(factory, entityId, entityId + 1);
                graph.Connect(entityId, entityId + 1);
            }
            factory.entityCursor = machineEntityId + 1;
            factory.factorySystem = new FactorySystem(factory.planet);
            var description = LDB.items.Select(2309);
            factory.entityPool[machineEntityId] = new EntityData
            {
                id = machineEntityId,
                protoId = 2309,
                modelIndex = (short)description.ModelIndex,
                assemblerId = 1,
            };
            factory.entityNeeds = new int[factory.entityPool.Length][];
            factory.entityMutexs = new Mutex[factory.entityPool.Length];
            factory.entityMutexs[machineEntityId] = new Mutex(machineEntityId);
            factory.entitySignPool = new SignData[factory.entityPool.Length];
            ref var assembler = ref factory.factorySystem.assemblerPool[1];
            assembler.id = 1;
            assembler.entityId = machineEntityId;
            assembler.speed = 10000;
            int recipeId = LDB
                .recipes.dataArray.Single(recipe =>
                    recipe.Results.Length == 1 && recipe.Results[0] == 1116
                )
                .ID;
            assembler.SetRecipe(recipeId, factory.entitySignPool);
            factory.entityNeeds[machineEntityId] = assembler.needs;

            Check(
                PipeApi.TryConnectMachinePort(
                    factory,
                    1,
                    machineEntityId,
                    0,
                    new MachinePortOptions(1000)
                ),
                "A machine endpoint binds a 100000-node pipe network."
            );
            Check(
                !PipeApi.TryInsert(factory, nodeCount, 1114, 1),
                "An enabled machine filter rejects a different item at remote nodes."
            );
            Check(
                PipeApi.TryGetInventory(factory, nodeCount, out var empty)
                    && empty.Count == 0
                    && empty.ItemId == 1000,
                "An enabled machine filter reserves remote empty nodes in a large network: "
                    + empty.ItemId
                    + "/"
                    + empty.Count
            );

            PipeApi.TryInsert(factory, nodeCount, 1000, 1, 4);
            PipeApi.TryTake(factory, 1, 1, out _, 1000);
            long visits = graph.TopologyVisits;
            long storageRevision = PipePools.StorageRevision(graph);
            const int operations = 100000;
            var clock = Stopwatch.StartNew();
            for (int index = 0; index < operations; index++)
            {
                if (
                    !PipeApi.TryInsert(factory, nodeCount, 1000, 1, 4)
                    || !PipeApi.TryTake(factory, 1, 1, out var amount, 1000)
                    || amount.Count != 1
                    || amount.ProliferatorPoints != 4
                )
                {
                    throw new InvalidOperationException("Filtered large-network I/O failed.");
                }
            }
            clock.Stop();
            Check(
                graph.TopologyVisits == visits
                    && PipePools.StorageRevision(graph) == storageRevision,
                "Stable machine-filtered I/O preserves topology and reservoir indexes over 200000 operations."
            );
            results.Add(
                "BENCH machine_filtered_native_io nodes="
                    + nodeCount
                    + " endpoints=1 ns_per_operation="
                    + (clock.Elapsed.TotalMilliseconds * 1000000 / (operations * 2)).ToString("F1")
            );

            PipeApi.TryInsert(factory, nodeCount, 1000, 100, 400);
            PipeMachinePorts.Tick(factory);
            int inputIndex = Array.IndexOf(assembler.recipeExecuteData.requires, 1000);
            Check(
                assembler.served[inputIndex] > 0
                    && PipePools.Query(graph, 1).Count + assembler.served[inputIndex] == 100
                    && PipePools.Query(graph, 1).ProliferatorPoints
                        + assembler.incServed[inputIndex]
                        == 400,
                "A large-network endpoint feeds native machine buffers with conserved stock and points."
            );
            int countBefore = assembler.served[inputIndex];
            enabledProperty.SetValue(null, false, null);
            assembler.served[inputIndex] = 0;
            PipeMachinePorts.Tick(factory);
            Check(
                assembler.served[inputIndex] == 0
                    && PipeApi.GetMachinePorts(factory, 1).Single().Options.ItemId == 1000,
                "Global disablement suspends established endpoint transfer and preserves its settings."
            );
            assembler.served[inputIndex] = countBefore;
        }
        finally
        {
            enabledProperty.SetValue(null, previouslyEnabled, null);
        }
    }
}
