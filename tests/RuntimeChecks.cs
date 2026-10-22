using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using BepInEx;
using FluidPipes;
using FluidPipes.Tests;
using HarmonyLib;
using UnityEngine;

[BepInPlugin("local.fluidpipes.checks", "Fluid Pipe Checks", "1.0.0")]
[BepInDependency(Plugin.Guid)]
public sealed partial class RuntimeChecks : BaseUnityPlugin
{
    private bool started;
    private readonly List<string> results = new List<string>();
    private int failures;
    private static bool syntheticRendering;
    private static long refundCount;
    private static long refundInc;
    private static Func<long> allocationCounter;

    private void Update()
    {
        if (started || !Prototypes.Ready || UIRoot.instance == null)
            return;
        started = true;
        StartCoroutine(Run());
    }

    private void Check(bool value, string name)
    {
        if (!value)
            throw new Exception(name);
        results.Add("PASS " + name);
    }

    private void Test(string name, Action run)
    {
        try
        {
            run();
        }
        catch (Exception error)
        {
            failures++;
            results.Add("FAIL " + name + " " + error);
        }
    }

    private static bool RenderPrefix() => !syntheticRendering;

    private static bool RefundPrefix(FluidAmount amount)
    {
        refundCount += amount.Count;
        refundInc += amount.ProliferatorPoints;
        return false;
    }

    private IEnumerator Run()
    {
        yield return null;
        if (Environment.GetEnvironmentVariable("FluidPipesRenderIcon") == "1")
        {
            Test(
                "orthographic item icon",
                () => PipeIconRender.Render(Path.GetDirectoryName(Info.Location))
            );
            Test(
                "orthographic valve icon",
                () => PipeIconRender.Render(Path.GetDirectoryName(Info.Location), true)
            );
            File.WriteAllLines(
                Path.Combine(Path.GetDirectoryName(Info.Location), "results.txt"),
                results.Concat(new[] { "Failures=" + failures })
            );
            Application.Quit(failures == 0 ? 0 : 1);
            yield break;
        }
        var counterMethod = typeof(GC).GetMethod(
            "GetAllocatedBytesForCurrentThread",
            Type.EmptyTypes
        );
        if (counterMethod != null)
        {
            var counter = (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), counterMethod);
            long before = counter();
            var probe = new byte[32768];
            long after = counter();
            GC.KeepAlive(probe);
            if (after > before)
                allocationCounter = counter;
        }
        results.Add(
            "INFO allocation counter " + (allocationCounter == null ? "unavailable" : "verified")
        );
        Test(
            "prototype",
            () =>
            {
                var item = LDB.items.Select(Prototypes.ItemId);
                Check(
                    item != null
                        && item.prefabDesc.isBelt
                        && item.ID != 2001
                        && item.ModelIndex == LDB.items.Select(2001).ModelIndex,
                    "independent pipe item with native belt geometry"
                );
                Check(
                    LDB.recipes.Select(Prototypes.RecipeId).Results[0] == item.ID,
                    "pipe recipe registration"
                );
                var beltRecipe = LDB.recipes.dataArray.First(recipePrototype =>
                    recipePrototype.Results.Contains(2001)
                );
                var tankRecipe = LDB.recipes.dataArray.First(recipePrototype2 =>
                    recipePrototype2.Results.Contains(2106)
                );
                var tankTech = LDB.techs.dataArray.First(t =>
                    t.UnlockRecipes != null && t.UnlockRecipes.Contains(tankRecipe.ID)
                );
                var buildbar = (ItemProto[,])
                    AccessTools.Field(typeof(UIBuildMenu), "protos").GetValue(null);
                foreach (int entityId in new[] { Prototypes.ItemId, Prototypes.ValveId })
                {
                    var proto = LDB.items.Select(entityId);
                    var craft = LDB.recipes.dataArray.Single(recipePrototype3 =>
                        recipePrototype3.Results.Contains(entityId)
                    );
                    Check(
                        craft.Items.SequenceEqual(beltRecipe.Items)
                            && craft.ItemCounts.SequenceEqual(beltRecipe.ItemCounts)
                            && craft.TimeSpend == beltRecipe.TimeSpend
                            && craft.ResultCounts[0]
                                == beltRecipe.ResultCounts[Array.IndexOf(beltRecipe.Results, 2001)],
                        "fluid building matches basic belt crafting batch " + entityId
                    );
                    Check(
                        proto.PreTechOverride == tankTech.ID
                            && tankTech.UnlockRecipes.Contains(craft.ID)
                            && Prototypes.UnlockRecipeId == tankRecipe.ID,
                        "fluid building follows tank technology " + entityId
                    );
                    int category = proto.BuildIndex / 100;
                    int slot = proto.BuildIndex % 100;
                    Check(
                        category >= 1
                            && category <= 8
                            && slot >= 1
                            && slot <= 10
                            && buildbar[category, slot]?.ID == entityId
                            && !LDB.items.dataArray.Any(other =>
                                other.ID != entityId && other.BuildIndex == proto.BuildIndex
                            ),
                        "fluid building occupies its own native buildbar slot " + entityId
                    );
                    var buttons = (UIButton[])
                        AccessTools
                            .Field(typeof(UIBuildMenu), "childButtons")
                            .GetValue(UIRoot.instance.uiGame.buildMenu);
                    Check(
                        slot < buttons.Length && buttons[slot] != null,
                        "fluid building has an instantiated buildbar button " + entityId
                    );
                    results.Add(
                        "INFO fluid_building="
                            + entityId
                            + " buildbar="
                            + category
                            + ":"
                            + slot
                            + " tech="
                            + tankTech.ID
                            + " "
                            + tankTech.name
                            + " ingredients="
                            + string.Join(
                                ",",
                                craft.Items.Select(
                                    (material, index) => craft.ItemCounts[index] + "x" + material
                                )
                            )
                            + " output="
                            + craft.ResultCounts[0]
                    );
                }

                Check(
                    LDB.items.Select(2001).Upgrades.All(entityId2 => entityId2 != item.ID),
                    "vanilla upgrade chain is unchanged"
                );
                var icon = item.iconSprite;
                var recipe = LDB.recipes.Select(Prototypes.RecipeId);
                Check(
                    icon != null
                        && icon != LDB.items.Select(2001).iconSprite
                        && recipe.iconSprite == icon,
                    "pipe item and recipe share an independent icon"
                );
                Check(
                    icon.texture.width == 80
                        && icon.texture.height == 80
                        && icon.texture.format == TextureFormat.ARGB32
                        && icon.texture.GetPixel(0, 0).a == 0
                        && icon.texture.GetPixels32().Any(p => p.a > 128),
                    "pipe icon preserves transparency and native atlas dimensions"
                );
                Check(
                    icon.texture.GetPixel(40, 40).a > 0.25f
                        && icon.texture.GetPixel(40, 40).a < 0.95f
                        && icon.texture.GetPixel(40, 40).b > 0.25f,
                    "pipe icon glass body remains translucent and visible at native inventory size"
                );
                var atlas = GameMain.iconSet;
                Check(
                    atlas.loaded
                        && atlas.itemIconIndex[item.ID] != 0
                        && atlas.itemIconIndex[item.ID] != atlas.itemIconIndex[2001]
                        && atlas.recipeIconIndex[recipe.ID] == atlas.itemIconIndex[item.ID],
                    "native item and recipe atlas indices reference the pipe icon"
                );
                File.WriteAllBytes(
                    Path.Combine(Path.GetDirectoryName(Info.Location), "pipe-item-icon.png"),
                    icon.texture.EncodeToPNG()
                );
                var valve = LDB.items.Select(Prototypes.ValveId).iconSprite;
                Check(
                    valve != null
                        && valve != icon
                        && valve != LDB.items.Select(2040).iconSprite
                        && LDB.recipes.Select(Prototypes.ValveRecipeId).iconSprite == valve,
                    "valve item and recipe share an independent valve icon"
                );
                Check(
                    valve.texture.width == 80
                        && valve.texture.height == 80
                        && valve.texture.format == TextureFormat.ARGB32
                        && valve.texture.GetPixel(0, 0).a == 0
                        && valve.texture.GetPixels32().Any(p => p.a > 128),
                    "valve icon preserves transparency and native atlas dimensions"
                );
                Check(
                    atlas.itemIconIndex[Prototypes.ValveId] != 0
                        && atlas.itemIconIndex[Prototypes.ValveId] != atlas.itemIconIndex[2040]
                        && atlas.itemIconIndex[Prototypes.ValveId] != atlas.itemIconIndex[item.ID]
                        && atlas.recipeIconIndex[Prototypes.ValveRecipeId]
                            == atlas.itemIconIndex[Prototypes.ValveId],
                    "native valve item and recipe atlas indices reference the valve icon"
                );
                File.WriteAllBytes(
                    Path.Combine(Path.GetDirectoryName(Info.Location), "pipe-valve-item-icon.png"),
                    valve.texture.EncodeToPNG()
                );
                ExportReferenceIcons();
            }
        );
        if (Environment.GetEnvironmentVariable("FluidPipesIconOnly") == "1")
        {
            File.WriteAllLines(
                Path.Combine(Path.GetDirectoryName(Info.Location), "results.txt"),
                results.Concat(new[] { "Failures=" + failures })
            );
            Application.Quit(failures == 0 ? 0 : 1);
            yield break;
        }
        Test("core in Unity Mono", () => results.Add(CoreSuite.Run(false)));
        Test("documented public API", ApiDocumentation);
        Test("public API and configurable item transport", ApiIntegration);
        yield return null;
        Test("patched native I/O", Integration);
        Test("native construction and path mutations", Construction);
        Test("native tank and station updates", Facilities);
        Test("mixed facility ports", MixedPorts);
        Test("shared connected liquid containers", SharedContainers);
        Test("two-space flow control valves", Valves);
        Test("unselected station input binds its fluid space", StationInputBinding);
        Test("undirected unlimited pipe interfaces", Undirected);
        Test("prebuild identifiers migrate without stale slots", PrebuildMigration);
        Test("legacy connection restoration", LegacyConnections);
        Test("connection cuts preserve nodes and inventories", ConnectionCuts);
        Test("pipe selection without native render batches", Selection);
        Test("procedural pipe geometry", Geometry);
        Test("raised fluid inserter geometry", FluidInserterGeometry);
        Test("opaque fittings behind transparent pipes", MetalOcclusion);
        Test(
            "procedural pipe gallery",
            () => PipeGallery.Create(Fixture, Path.GetDirectoryName(Info.Location))
        );
        Test(
            "glass interface ordering",
            () =>
            {
                var outer = PipeVisuals.BodyMaterial;
                var inner = (Material)
                    AccessTools.Field(typeof(PipeVisuals), "innerBody").GetValue(null);
                Check(
                    inner != null
                        && inner != outer
                        && inner.renderQueue < outer.renderQueue
                        && inner.shader == outer.shader
                        && inner.color == outer.color
                        && inner.GetColor("_RimColor") == outer.GetColor("_RimColor"),
                    "glass inner interfaces precede matching outer interfaces"
                );
                var builder = new PipeMesh();
                builder.Node(
                    Vector3.zero,
                    Vector3.up,
                    Vector3.right,
                    new[] { Vector3.left, Vector3.right },
                    new[] { false, false }
                );
                var mesh = builder.Build("FluidPipes.GlassOrderCheck", true);
                try
                {
                    Check(
                        mesh.subMeshCount == 3
                            && mesh.GetTriangles(0).Length > 0
                            && mesh.GetTriangles(2).Length == mesh.GetTriangles(0).Length,
                        "glass interface geometry uses separate render passes"
                    );
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(mesh);
                }
            }
        );
        Test("liquid render cache", RenderCache);
        Test("native I/O benchmark", Benchmark);
        Test("large shared-container I/O benchmark", SharedLargeBenchmark);
        Test("native inserter workload", Inserters);
        string output = Path.GetDirectoryName(Info.Location);
        File.WriteAllLines(
            Path.Combine(output, "results.txt"),
            results.Concat(new[] { "Failures=" + failures })
        );
        Logger.LogInfo("Fluid pipe checks complete failures=" + failures);
        Application.Quit(failures == 0 ? 0 : 1);
    }

    private void ExportReferenceIcons()
    {
        string directory = Path.Combine(Path.GetDirectoryName(Info.Location), "icon-reference");
        Directory.CreateDirectory(directory);
        foreach (int entityId in new[] { 2001, 2002, 2011, 2106, 1103, 1110, 1107, 1206 })
        {
            var item = LDB.items.Select(entityId);
            var sprite = item.iconSprite;
            var rect = sprite.textureRect;
            var target = RenderTexture.GetTemporary(80, 80, 0, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            var image = new Texture2D(80, 80, TextureFormat.RGBA32, false);
            try
            {
                Graphics.Blit(
                    sprite.texture,
                    target,
                    new Vector2(
                        rect.width / sprite.texture.width,
                        rect.height / sprite.texture.height
                    ),
                    new Vector2(rect.x / sprite.texture.width, rect.y / sprite.texture.height)
                );
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 80, 80), 0, 0);
                image.Apply();
                File.WriteAllBytes(Path.Combine(directory, entityId + ".png"), image.EncodeToPNG());
                results.Add("REFERENCE icon " + entityId + " " + item.Name);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.Destroy(image);
            }
        }
        var comparison = new Texture2D(520, 112, TextureFormat.RGBA32, false);
        try
        {
            comparison.SetPixels32(
                Enumerable.Repeat(new Color32(35, 44, 50, 255), 520 * 112).ToArray()
            );
            int column = 0;
            foreach (
                int entityId2 in new[] { 2001, 2011, 2106, Prototypes.ItemId, Prototypes.ValveId }
            )
            {
                var image = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                try
                {
                    image.LoadImage(
                        entityId2 == Prototypes.ItemId || entityId2 == Prototypes.ValveId
                            ? LDB.items.Select(entityId2).iconSprite.texture.EncodeToPNG()
                            : File.ReadAllBytes(Path.Combine(directory, entityId2 + ".png"))
                    );
                    for (int y = 0; y < 80; y++)
                    for (int x = 0; x < 80; x++)
                    {
                        var pixel = image.GetPixel(x, y);
                        int px = 12 + column * 104 + x;
                        int py = 16 + y;
                        comparison.SetPixel(
                            px,
                            py,
                            Color.Lerp(
                                comparison.GetPixel(px, py),
                                new Color(pixel.r, pixel.g, pixel.b, 1),
                                pixel.a
                            )
                        );
                    }
                }
                finally
                {
                    UnityEngine.Object.Destroy(image);
                }
                column++;
            }
            comparison.Apply();
            File.WriteAllBytes(
                Path.Combine(Path.GetDirectoryName(Info.Location), "pipe-icon-comparison.png"),
                comparison.EncodeToPNG()
            );
        }
        finally
        {
            UnityEngine.Object.Destroy(comparison);
        }
    }

    private static PlanetFactory Fixture(int size, int planetId, bool paths)
    {
        var planet = new PlanetData
        {
            id = planetId,
            radius = 200,
            scale = 1,
        };
        var factory = new PlanetFactory
        {
            entityPool = new EntityData[size + 20],
            entityCursor = size + 1,
            entityConnPool = new int[(size + 20) * 16],
            prebuildPool = new PrebuildData[20],
            prebuildConnPool = new int[320],
            cargoContainer = new CargoContainer(),
        };
        AccessTools.Property(typeof(PlanetFactory), "planet").SetValue(factory, planet, null);
        planet.factory = factory;
        var traffic = new CargoTraffic(planet);
        factory.cargoTraffic = traffic;
        for (int entityId = 1; entityId <= size; entityId++)
        {
            factory.entityPool[entityId] = new EntityData
            {
                id = entityId,
                protoId = (short)Prototypes.ItemId,
                modelIndex = (short)LDB.items.Select(Prototypes.ItemId).ModelIndex,
                pos = new Vector3((entityId - 1) * 1.2f, 200, 0).normalized * 200,
                rot = Quaternion.identity,
            };
            int belt = traffic.NewBeltComponent(entityId, 1);
            if (paths)
            {
                if ((entityId - 1) % 100 == 0)
                {
                    var path = traffic.NewCargoPath();
                    path.AddBuffer(
                        100,
                        1,
                        new Vector3[100],
                        Enumerable.Repeat(Quaternion.identity, 100).ToArray(),
                        Vector3.zero
                    );
                }
                var current = traffic.pathPool[traffic.pathCursor - 1];
                current.AddRelatedBelt(belt);
                traffic.beltPool[belt].SetSegment(current.id, 0, 40, 100);
                if (entityId > 1)
                    PipeRuntime.Bind(traffic).Connect(entityId - 1, entityId);
            }
        }
        return factory;
    }

    private void Integration()
    {
        PipeRuntime.Clear();
        var factory = Fixture(250, 900001, true);
        var traffic = factory.cargoTraffic;
        var graph = PipeRuntime.Bind(traffic);
        byte remain;
        byte stack;
        byte proliferatorPoints;
        Check(
            ItemProto.isFluid(1000) && ItemProto.isFluid(1007),
            "native fluid IDs water and crude oil"
        );
        Check(
            factory.InsertInto(1, 0, 1000, 4, 16, out remain) == 4 && remain == 0,
            "factory entity insertion"
        );
        Check(
            factory.PickFrom(
                (uint)(0x1000000 | 250),
                0,
                1000,
                null,
                out stack,
                out proliferatorPoints
            ) == 1000
                && stack == 1
                && proliferatorPoints == 4,
            "cached typed target remote extraction"
        );
        Check(
            factory.InsertInto((uint)(0x1000000 | 150), 0, 1007, 1, 0, out remain) == 0,
            "mixed fluid rejected through typed target"
        );
        Check(factory.InsertInto(1, 0, 1101, 1, 0, out remain) == 0, "solid rejected");
        Check(
            traffic.TryPickItemAtRear(250, 1007, null, out stack, out proliferatorPoints) == 0,
            "direct facility filter"
        );
        Check(traffic.TryInsertItemAtHead(150, 1000, 2, 8), "direct facility head insertion");
        Check(
            traffic.TryPickItemAtRear(250, 0, new[] { 1000 }, out stack, out proliferatorPoints)
                == 1000,
            "direct facility rear extraction"
        );
        var path = traffic.GetCargoPath(traffic.beltPool[250].segPathId);
        Check(
            path.TryUpdateItemAtHeadAndFillBlank(1000, 4, 2, 8),
            "station and fractionator direct path insertion"
        );
        Check(
            path.TryPickItemAtRear(
                new[] { 1007, 1000 },
                out int need,
                out stack,
                out proliferatorPoints
            ) == 1000
                && need == 1,
            "station direct path needs selection"
        );
        Check(
            traffic.GetItemIdAtRear(250) == 1000
                && graph.View(250).Count == 5
                && graph.View(250).ProliferatorPoints == 20,
            "direct queries and exact inventory"
        );
        Check(traffic.container.cursor == 0, "pipe transfers allocate no physical cargo");
        Check(
            factory.PickFrom(1, 0, 1007, null, out stack, out proliferatorPoints) == 0,
            "entity target filter"
        );
        Check(
            !PipeRuntime.CanLink(factory, 1, AddObject(factory, 251, 2001)),
            "pipe and ordinary belt isolation"
        );
        Check(
            !PipeRuntime.CanLink(factory, 1, AddObject(factory, 252, 2020)),
            "pipe and splitter isolation"
        );
        Check(
            !PipeRuntime.CanLink(factory, 1, AddObject(factory, 253, 2040)),
            "pipe and piler isolation"
        );
        int tank = AddObject(factory, 254, 2106);
        Check(PipeRuntime.CanLink(factory, 1, tank), "native tank port allowed");
        factory.WriteObjectConn(tank, 0, false, 1, 0);
        factory.WriteObjectConn(tank, 1, true, 251, 0);
        factory.ReadObjectConn(tank, 1, out _, out int connected, out _);
        Check(connected == 251, "tank permits ordinary belts alongside pipes");
        factory.WriteObjectConn(1, 2, true, 251, 1);
        factory.ReadObjectConn(1, 2, out _, out connected, out _);
        Check(connected == 0, "mixed facilities do not permit direct pipe-to-belt links");
        using (var memory = new MemoryStream())
        {
            PipeRuntime.Export(new BinaryWriter(memory));
            memory.Position = 0;
            PipeRuntime.Import(new BinaryReader(memory));
            Check(PipeRuntime.Bind(traffic).View(250).Count == 5, "mod save roundtrip");
            Check(
                traffic.TryPickItemAtRear(250, 1000, null, out stack, out proliferatorPoints)
                    == 1000,
                "owner binding after load"
            );
        }
        var normal = Fixture(1, 900002, true);
        normal.entityPool[1].protoId = 2001;
        Check(
            normal.cargoTraffic.TryInsertItem(1, 0, 1101, 4, 16),
            "ordinary belt insertion remains native"
        );
        Check(
            normal.cargoTraffic.TryPickItem(1, 0, 1101, out stack, out proliferatorPoints) == 1101
                && stack == 4
                && proliferatorPoints == 16,
            "ordinary cargo stacking remains native"
        );
    }

    private static int AddObject(PlanetFactory factory, int entityId, int proto)
    {
        factory.entityPool[entityId] = new EntityData
        {
            id = entityId,
            protoId = (short)proto,
            modelIndex = (short)LDB.items.Select(proto).ModelIndex,
        };
        factory.entityCursor = Math.Max(factory.entityCursor, entityId + 1);
        return entityId;
    }

    private void Construction()
    {
        var harmony = new Harmony("local.fluidpipes.synthetic-rendering");
        refundCount = refundInc = 0;
        harmony.Patch(
            AccessTools.Method(typeof(PipeRuntime), nameof(PipeRuntime.Return)),
            prefix: new HarmonyMethod(typeof(RuntimeChecks), nameof(RefundPrefix))
        );
        foreach (
            string name in new[]
            {
                "AlterBeltRenderer",
                "AlterPathRenderer",
                "RemoveBeltRenderer",
                "RemovePathRenderer",
                "RefreshPathUV",
            }
        )
            harmony.Patch(
                AccessTools.Method(typeof(CargoTraffic), name),
                prefix: new HarmonyMethod(typeof(RuntimeChecks), nameof(RenderPrefix))
            );
        syntheticRendering = true;
        try
        {
            var factory = Fixture(8, 900003, false);
            var traffic = factory.cargoTraffic;
            for (int index = 1; index <= 8; index++)
                traffic.AlterBeltConnections(index, 0, 0, 0, 0);
            for (int index2 = 1; index2 < 8; index2++)
            {
                var b = traffic.beltPool[index2];
                traffic.AlterBeltConnections(
                    index2,
                    index2 + 1,
                    b.backInputId,
                    b.leftInputId,
                    b.rightInputId
                );
            }
            var graph = PipeRuntime.Bind(traffic);
            Check(graph.View(8).Nodes == 8, "native path concatenation forms shared network");
            Check(
                traffic.TryInsertItem(1, 0, 1000, 4, 16)
                    && traffic.TryPickItem(8, 0, 1000, out _, out _) == 1000,
                "native constructed endpoints exchange immediately"
            );
            var last = traffic.beltPool[8];
            traffic.AlterBeltConnections(
                8,
                1,
                last.backInputId,
                last.leftInputId,
                last.rightInputId
            );
            Check(
                graph.View(1).Nodes == 8
                    && traffic.GetCargoPath(traffic.beltPool[1].segPathId).closed,
                "native closed belt loop shares inventory"
            );
            last = traffic.beltPool[8];
            traffic.AlterBeltConnections(
                8,
                0,
                last.backInputId,
                last.leftInputId,
                last.rightInputId
            );
            Check(graph.View(1).Count == 3, "opening native belt loop preserves inventory");
            traffic.RemoveBeltComponent(4);
            Check(
                graph.View(1).Nodes == 3 && graph.View(8).Nodes == 4 && graph.View(4).Nodes == 0,
                "native dismantling splits network and removes node"
            );
            Check(
                graph.View(1).Count + graph.View(8).Count + refundCount == 3
                    && graph.View(1).ProliferatorPoints
                        + graph.View(8).ProliferatorPoints
                        + refundInc
                        == 12,
                "native dismantling conserves stock and points including refunds"
            );
            int recycled = traffic.NewBeltComponent(4, 1);
            traffic.AlterBeltConnections(recycled, 0, 0, 0, 0);
            Check(
                graph.View(4).Nodes == 1 && graph.View(4).Count == 0,
                "recycled belt starts empty"
            );
            traffic.TryInsertItem(recycled, 0, 1007, 1, 0);
            var before = graph.View(8);
            var b4 = traffic.beltPool[recycled];
            traffic.AlterBeltConnections(
                recycled,
                5,
                b4.backInputId,
                b4.leftInputId,
                b4.rightInputId
            );
            Check(
                graph.View(4).Item == 1007
                    && graph.View(8).Item == 1000
                    && graph.View(8).Count == before.Count,
                "native connection rejects incompatible fluids"
            );
            var branch = Fixture(5, 900004, false);
            var bt = branch.cargoTraffic;
            for (int index3 = 1; index3 <= 5; index3++)
                bt.AlterBeltConnections(index3, 0, 0, 0, 0);
            bt.AlterBeltConnections(1, 3, 0, 0, 0);
            bt.AlterBeltConnections(2, 3, 0, 0, 0);
            var junction = bt.beltPool[3];
            bt.AlterBeltConnections(
                3,
                4,
                junction.backInputId,
                junction.leftInputId,
                junction.rightInputId
            );
            var graph2 = PipeRuntime.Bind(bt);
            Check(
                graph2.View(1).Nodes == 4 && graph2.View(2).Nodes == 4 && graph2.View(5).Nodes == 1,
                "native merging junction spans cargo paths"
            );
            bt.TryInsertItem(1, 0, 1000, 4, 16);
            Check(
                bt.TryPickItem(2, 0, 1000, out _, out _) == 1000,
                "junction inventory is accessible against belt direction"
            );
            bt.AlterBeltConnections(2, 5, 0, 0, 0);
            Check(
                graph2.View(1).Nodes == 3
                    && graph2.View(2).Nodes == 2
                    && graph2.View(1).Count + graph2.View(2).Count == 3,
                "native branch rerouting preserves stock"
            );
        }
        finally
        {
            syntheticRendering = false;
            harmony.UnpatchSelf();
        }
    }

    private void Benchmark()
    {
        foreach (int size in new[] { 10, 1000, 100000 })
        {
            var factory = Fixture(size, 910000 + size, true);
            var graph = PipeRuntime.Bind(factory.cargoTraffic);
            const int loops = 200000;
            var samples = new List<double>();
            var allocated = allocationCounter;
            long allocatedBytes = 0;
            for (int index = 0; index < 10000; index++)
            {
                factory.InsertInto(1, 0, 1000, 1, 4, out _);
                factory.PickFrom(size, 0, 1000, null, out _, out _);
            }
            long visits = graph.TopologyVisits;
            for (int sample = 0; sample < 5; sample++)
            {
                var watch = Stopwatch.StartNew();
                long allocatedStart = allocated == null ? 0 : allocated();
                for (int index2 = 0; index2 < loops; index2++)
                {
                    int source = 1 + (int)((long)index2 * 104729 % size);
                    if (
                        factory.InsertInto(source, 0, 1000, 1, 4, out _) != 1
                        || factory.PickFrom(size + 1 - source, 0, 1000, null, out _, out _) != 1000
                    )
                        throw new Exception("native benchmark I/O failed");
                }
                if (allocated != null)
                    allocatedBytes += allocated() - allocatedStart;
                watch.Stop();
                samples.Add(watch.Elapsed.TotalMilliseconds * 1000000 / (loops * 2));
            }
            samples.Sort();
            Check(
                graph.TopologyVisits == visits && graph.View(1).Count == 0,
                "native steady I/O does not rebuild graph nodes=" + size
            );
            results.Add(
                "BENCH native_factory_io nodes="
                    + size
                    + " median_ns_per_op="
                    + samples[2].ToString("F1")
                    + " min="
                    + samples[0].ToString("F1")
                    + " max="
                    + samples[4].ToString("F1")
                    + " allocated_bytes="
                    + (allocated == null ? "unavailable" : allocatedBytes.ToString())
            );
        }
    }

    private static void AttachStation(PlanetFactory factory, StationComponent station)
    {
        var transport = (PlanetTransport)
            System.Runtime.Serialization.FormatterServices.GetUninitializedObject(
                typeof(PlanetTransport)
            );
        transport.factory = factory;
        transport.planet = factory.planet;
        transport.stationPool = new StationComponent[3];
        transport.stationCursor = 2;
        station.id = 1;
        transport.stationPool[1] = station;
        factory.transport = transport;
        PipePools.Dirty(factory);
    }

    private void SharedLargeBenchmark()
    {
        const int size = 100000;
        const int loops = 200000;
        var factory = Fixture(size, 970001, true);
        var graph = PipeRuntime.Bind(factory.cargoTraffic);
        factory.factoryStorage = new FactoryStorage(factory.planet);
        factory.factoryStorage.tankCursor = 2;
        factory.factoryStorage.tankPool[1] = new TankComponent
        {
            id = 1,
            entityId = size + 1,
            belt0 = 1,
            fluidCapacity = 10000,
            fluidId = 1000,
            fluidCount = 1000,
            fluidInc = 4000,
        };
        PipePools.View(graph, 1, true);
        long visits = graph.TopologyVisits;
        var samples = new List<double>();
        var allocated = allocationCounter;
        long bytes = 0;
        for (int sample = 0; sample < 3; sample++)
        {
            long before = allocated == null ? 0 : allocated();
            var watch = Stopwatch.StartNew();
            for (int index = 0; index < loops; index++)
            {
                if (
                    factory.InsertInto(1, 0, 1000, 1, 4, out _) != 1
                    || factory.PickFrom(size, 0, 1000, null, out _, out _) != 1000
                )
                    throw new Exception("Large shared native I/O failed.");
            }
            watch.Stop();
            samples.Add(watch.Elapsed.TotalMilliseconds * 1000000 / (loops * 2));
            if (allocated != null)
                bytes += allocated() - before;
        }
        var view = PipePools.View(graph, size, true);
        samples.Sort();
        Check(
            view.Count == 1000
                && view.ProliferatorPoints == 4000
                && view.Capacity == 10010000
                && graph.TopologyVisits == visits,
            "100000-node shared storage I/O preserves stock without topology traversal"
        );
        results.Add(
            "BENCH shared_large_native_io nodes="
                + size
                + " containers=1 median_ns_per_op="
                + samples[1].ToString("F1")
                + " allocated_bytes="
                + (allocated == null ? "unavailable" : bytes.ToString())
        );
    }

    private void Facilities()
    {
        var factory = Fixture(10, 900005, true);
        var traffic = factory.cargoTraffic;
        var graph = PipeRuntime.Bind(traffic);
        factory.factoryStorage = new FactoryStorage(factory.planet);
        factory.factoryStorage.tankCursor = 2;
        var tanks = factory.factoryStorage.tankPool;
        tanks[1] = new TankComponent
        {
            id = 1,
            entityId = 11,
            isBottom = true,
            inputSwitch = true,
            outputSwitch = true,
            fluidCapacity = 10000,
            belt0 = 10,
            fluidId = 1000,
            fluidCount = 4400,
            fluidInc = 17600,
        };
        var view = PipePools.View(graph, 1, true);
        Check(
            view.Capacity == 11000 && graph.View(1).Count == 400 && tanks[1].fluidCount == 4000,
            "tank and pipe capacity share a common fill percentage"
        );
        tanks[1].GameTick(factory);
        tanks[1].isOutput0 = true;
        tanks[1].GameTick(factory);
        Check(
            tanks[1].fluidCount == 4000 && graph.View(1).Count == 400,
            "native tank port direction does not duplicate shared fluid transfers"
        );
        var station = new StationComponent
        {
            storage = new[]
            {
                new StationStore
                {
                    itemId = 1000,
                    count = 1000,
                    inc = 4000,
                    max = 10000,
                },
            },
            slots = new[]
            {
                new SlotData
                {
                    beltId = 1,
                    dir = IODir.Output,
                    storageIdx = 1,
                },
            },
            needs = new int[6],
        };
        AttachStation(factory, station);
        view = PipePools.View(graph, 1, true);
        Check(
            view.Capacity == 21000
                && view.Count == 5400
                && view.ProliferatorPoints == 21600
                && view.Containers == 2,
            "tower storage joins the tank and pipe inventory without duplication"
        );
        station.UpdateOutputSlots(traffic, null, 4, false);
        station.slots[0].dir = IODir.Input;
        station.needs[0] = 1000;
        station.UpdateInputSlots(traffic, null, false);
        Check(
            PipePools.View(graph, 1, true).Count == 5400,
            "native station input and output do not recirculate shared inventory"
        );
        int splitter = traffic.NewSplitterComponent(AddObject(factory, 12, 2020));
        traffic.ConnectToSplitter(splitter, 1, 0, true);
        Check(traffic.splitterPool[splitter].input0 == 0, "runtime splitter connection guard");
    }

    private void SharedContainers()
    {
        var factory = Fixture(2, 960001, true);
        var graph = PipeRuntime.Bind(factory.cargoTraffic);
        graph.Disconnect(1, 2);
        factory.factoryStorage = new FactoryStorage(factory.planet);
        var tanks = factory.factoryStorage.tankPool;
        factory.factoryStorage.tankCursor = 3;
        tanks[1] = new TankComponent
        {
            id = 1,
            entityId = 3,
            belt0 = 1,
            belt1 = 2,
            nextTankId = 2,
            fluidCapacity = 1000,
            fluidId = 1000,
            fluidCount = 1000,
            fluidInc = 4000,
        };
        tanks[2] = new TankComponent
        {
            id = 2,
            entityId = 4,
            lastTankId = 1,
            fluidCapacity = 1000,
            fluidId = 1000,
            fluidCount = 50,
            fluidInc = 200,
        };
        var station = new StationComponent
        {
            storage = new[]
            {
                new StationStore { itemId = 1000, max = 2000 },
                new StationStore
                {
                    itemId = 1101,
                    max = 1000,
                    count = 123,
                    inc = 492,
                },
            },
            slots = new[]
            {
                new SlotData
                {
                    beltId = 1,
                    dir = IODir.Output,
                    storageIdx = 1,
                },
                new SlotData
                {
                    beltId = 2,
                    dir = IODir.Input,
                    storageIdx = 1,
                },
            },
            needs = new int[6],
        };
        AttachStation(factory, station);
        var inventoryView = PipePools.View(graph, 1, true);
        Check(
            inventoryView.Capacity == 4200
                && inventoryView.Count == 1050
                && inventoryView.Nodes == 2
                && inventoryView.Containers == 3
                && PipePools.SameSpace(graph, 1, 2),
            "tank stacks and a tower slot join disjoint pipe components once per container"
        );
        Check(
            graph.View(1).Count == 25
                && graph.View(2).Count == 25
                && tanks[1].fluidCount == 250
                && tanks[2].fluidCount == 250
                && station.storage[0].count == 500,
            "all connected storage volumes hold the same 25 percent fill"
        );
        int requested = 700;
        int item = 1000;
        station.TakeItem(ref item, ref requested, out int extractedInc);
        Check(
            requested == 700 && extractedInc == 2800 && PipePools.View(graph, 1, true).Count == 350,
            "native station extraction reads beyond its local storage share"
        );
        PipePools.Insert(graph, 1, 1000, 700, 2800);
        PipePools.View(graph, 1, true);
        var amount = PipePools.Take(graph, 2, 700);
        inventoryView = PipePools.View(graph, 1, true);
        Check(
            amount.Count == 700
                && amount.ProliferatorPoints == 2800
                && inventoryView.Count == 350
                && inventoryView.ProliferatorPoints == 1400,
            "extraction accesses shared containers beyond the local pipe inventory"
        );
        Check(
            PipePools.Insert(graph, 1, 1000, 150, 600)
                && PipePools.View(graph, 2, true).Count == 500,
            "insertion through either component updates the same fluid space"
        );
        using (var stream = new MemoryStream())
        {
            PipeRuntime.Export(new BinaryWriter(stream));
            stream.Position = 0;
            PipeRuntime.Import(new BinaryReader(stream));
            graph = PipeRuntime.Bind(factory.cargoTraffic);
            Check(
                PipePools.View(graph, 2, true).Count == 500,
                "restoration counts native container stocks exactly once"
            );
        }
        PipePools.Empty(graph, 1);
        inventoryView = PipePools.View(graph, 2, true);
        Check(
            inventoryView.Count == 0
                && tanks[1].fluidCount == 0
                && tanks[2].fluidCount == 0
                && station.storage[0].count == 0
                && station.storage[1].count == 123,
            "clearing a shared liquid space preserves unrelated tower slots"
        );
        int ordinary = OrdinaryBelt(factory, 5);
        tanks[1].belt2 = ordinary;
        tanks[1].isOutput2 = true;
        tanks[1].outputSwitch = true;
        tanks[1].isBottom = true;
        PipePools.Insert(graph, 1, 1000, 1, 4);
        PipePools.View(graph, 1, true);
        Check(
            tanks[1].fluidCount == 0,
            "integer fill allocation can leave a connected tank locally empty"
        );
        tanks[1].GameTick(factory);
        var path = factory.cargoTraffic.GetCargoPath(
            factory.cargoTraffic.beltPool[ordinary].segPathId
        );
        Check(
            path.TryPickItem(0, 100, out byte stack, out byte proliferatorPoints) == 1000
                && stack == 1
                && proliferatorPoints == 4
                && PipePools.View(graph, 1, true).Count == 0,
            "ordinary tank output extracts from the shared space even when its local share is empty"
        );
        PipePools.Insert(graph, 1, 1000, 420, 1680);
        inventoryView = PipePools.View(graph, 2, true);
        tanks[1].belt1 = 0;
        station.slots[1].beltId = 0;
        PipePools.Dirty(factory);
        Check(
            !PipePools.SameSpace(graph, 1, 2)
                && PipePools.View(graph, 1, true).Count + PipePools.View(graph, 2, true).Count
                    == 420,
            "disconnecting a shared storage bridge separates spaces without losing fluid"
        );
        var watch = Stopwatch.StartNew();
        for (int index = 0; index < 100000; index++)
        {
            if (!PipePools.Insert(graph, 1, 1000, 1, 4) || PipePools.Take(graph, 1, 1).Count != 1)
                throw new Exception("shared storage I/O failed");
        }
        watch.Stop();
        results.Add(
            "BENCH shared_container_io operations=200000 ns_per_op="
                + (watch.Elapsed.TotalMilliseconds * 1000000 / 200000).ToString("F1")
        );
    }

    private void StationInputBinding()
    {
        var factory = Fixture(1, 960003, true);
        var graph = PipeRuntime.Bind(factory.cargoTraffic);
        var station = new StationComponent
        {
            storage = new[]
            {
                new StationStore { itemId = 1000, max = 1000 },
                new StationStore { itemId = 1007, max = 1000 },
            },
            slots = new[]
            {
                new SlotData { beltId = 1, dir = IODir.Input },
            },
            needs = new int[6],
        };
        AttachStation(factory, station);
        Check(
            PipePools.Insert(graph, 1, 1000, 500, 2000),
            "first liquid selects the matching tower input before checking shared capacity"
        );
        var view = PipePools.View(graph, 1, true);
        Check(
            view.Count == 500
                && view.Capacity == 1100
                && station.slots[0].storageIdx == 1
                && station.storage[1].count == 0,
            "automatic input binding includes only the matching liquid storage slot"
        );
    }

    private void Valves()
    {
        var factory = Fixture(2, 960002, true);
        var graph = PipeRuntime.Bind(factory.cargoTraffic);
        int entity = AddObject(factory, 3, Prototypes.ValveId);
        int splitter = factory.cargoTraffic.NewSplitterComponent(entity);
        factory.powerSystem = new PowerSystem(factory.planet);
        int pc = ValvePower.Ensure(factory, entity);
        factory.WriteObjectConn(entity, 0, false, 1, 0);
        factory.WriteObjectConn(entity, 1, true, 2, 1);
        Check(
            !PipePools.SameSpace(graph, 1, 2)
                && LDB.items.Select(Prototypes.ValveId).prefabDesc.portPoses.Length == 2,
            "valve has exactly two ports and does not merge its adjacent spaces"
        );
        graph.Insert(1, 1000, 80, 320);
        var config = PipeValves.Get(factory, entity);
        config.Rate = 12.5;
        Check(
            LDB.items.Select(Prototypes.ValveId).prefabDesc.isPowerConsumer
                && factory.powerSystem.consumerPool[pc].workEnergyPerTick == 1667,
            "valve registers a native approximately 100 kW power consumer"
        );
        Check(
            PipeValves.Step(factory, entity, 1) == 0 && graph.View(2).Count == 0,
            "unconnected valve cannot pump"
        );
        int network = factory.powerSystem.NewNetwork();
        factory.powerSystem.OnConsumerRemoving(pc);
        factory.powerSystem.consumerPool[pc].networkId = network;
        factory.powerSystem.netPool[network].consumers.Add(pc);
        Check(
            PipeValves.Step(factory, entity, 1) == 0
                && factory.powerSystem.consumerPool[pc].requiredEnergy == 1667,
            "unpowered network stops transfer while requesting pump power"
        );
        factory.powerSystem.networkServes[network] = 0.5f;
        for (int index = 0; index < 60; index++)
            PipeValves.Step(factory, entity, 1.0 / 60);
        Check(
            graph.View(2).Count == 6 && graph.View(1).Count + graph.View(2).Count == 80,
            "half power proportionally limits fractional valve throughput"
        );
        factory.powerSystem.networkServes[network] = 0;
        Check(
            PipeValves.Step(factory, entity, 60) == 0 && graph.View(2).Count == 6,
            "power outage cannot accumulate pump credit"
        );
        graph.SetInventory(1, 1000, 80, 320);
        graph.SetInventory(2, 0, 0, 0);
        factory.powerSystem.networkServes[network] = 1;
        for (int index2 = 0; index2 < 60; index2++)
            PipeValves.Step(factory, entity, 1.0 / 60);
        Check(
            graph.View(2).Count == 12
                && graph.View(1).Count + graph.View(2).Count == 80
                && graph.View(1).ProliferatorPoints + graph.View(2).ProliferatorPoints == 320,
            "valve honors fractional per-second rate and conserves proliferation"
        );
        config.Enabled = false;
        PipeValves.Step(factory, entity, 1);
        Check(graph.View(2).Count == 12, "closed valve isolates both spaces");
        Check(
            factory.powerSystem.consumerPool[pc].requiredEnergy == 0,
            "closed valve draws no standby power"
        );
        config.Enabled = true;
        config.Direction = ValveDirection.AtoB;
        graph.SetInventory(1, 1000, 10, 40);
        graph.SetInventory(2, 1000, 70, 280);
        Check(
            PipeValves.Step(factory, entity, 1) == 0 && graph.View(1).Count == 10,
            "one-way valve rejects reverse flow"
        );
        config.Direction = ValveDirection.Both;
        config.Level = ValveLevel.KeepA;
        config.Target = 0.5;
        config.Rate = 60;
        for (int index3 = 0; index3 < 60; index3++)
            PipeValves.Step(factory, entity, 1.0 / 60);
        Check(
            graph.View(1).Count == 50 && graph.View(2).Count == 30,
            "valve maintains the selected A-side fill level"
        );
        graph.SetInventory(1, 1000, 80, 320);
        graph.SetInventory(2, 0, 0, 0);
        config.Level = ValveLevel.KeepB;
        config.Target = 0.75;
        for (int index4 = 0; index4 < 100; index4++)
            PipeValves.Step(factory, entity, 1.0 / 60);
        Check(
            graph.View(1).Count == 5 && graph.View(2).Count == 75,
            "valve maintains the selected B-side fill level"
        );
        graph.SetInventory(2, 1007, 50, 0);
        Check(
            PipeValves.Step(factory, entity, 1) == 0 && graph.View(1).Count == 5,
            "valve rejects incompatible fluids"
        );
        Check(
            factory.powerSystem.consumerPool[pc].requiredEnergy == 0,
            "incompatible fluids do not request pump power"
        );
        factory.cargoTraffic.UpdateSplitter(ref factory.cargoTraffic.splitterPool[splitter]);
        Check(
            graph.View(1).Count == 5 && graph.View(2).Count == 50,
            "native splitter logic cannot bypass valve control"
        );
        OrdinaryBelt(factory, 4);
        Check(!PipeRuntime.CanLink(factory, entity, 4), "valve rejects an ordinary belt");
        factory.WriteObjectConn(entity, 2, true, 1, 1);
        factory.ReadObjectConn(entity, 2, out _, out int illegal, out _);
        Check(illegal == 0, "valve rejects a third pipe port");
        config.Rate = 12.5;
        config.Target = 0.35;
        config.Direction = ValveDirection.BtoA;
        config.Level = ValveLevel.KeepA;
        using (var stream = new MemoryStream())
        {
            PipeRuntime.Export(new BinaryWriter(stream));
            stream.Position = 0;
            PipeRuntime.Import(new BinaryReader(stream));
            var restored = PipeValves.Get(factory, entity);
            Check(
                restored.Rate == 12.5
                    && restored.Target == 0.35
                    && restored.Direction == ValveDirection.BtoA
                    && restored.Level == ValveLevel.KeepA,
                "valve settings survive save restoration"
            );
        }
        factory.powerSystem.RemoveConsumerComponent(pc);
        factory.entityPool[entity].powerConId = 0;
        ValvePower.Restore(factory);
        int migrated = factory.entityPool[entity].powerConId;
        ValvePower.Restore(factory);
        Check(
            migrated > 0
                && factory.entityPool[entity].powerConId == migrated
                && factory.powerSystem.consumerCount == 1,
            "legacy valves gain exactly one native power consumer"
        );
    }

    private void Inserters()
    {
        const int size = 100000;
        const int arms = 10000;
        const int ticks = 240;
        var factory = Fixture(size, 920001, true);
        var graph = PipeRuntime.Bind(factory.cargoTraffic);
        factory.entitySignPool = new SignData[size + 20];
        var animation = new AnimData[arms + 1];
        var needs = new int[size + 20][];
        var inserters = new InserterComponent[arms];
        graph.Insert(1, 1000, arms * 2, arms * 8);
        for (int index = 0; index < arms; index++)
            inserters[index] = new InserterComponent
            {
                id = index + 1,
                entityId = index + 1,
                stage = EInserterStage.Picking,
                stt = 10000,
                delay = 10000,
                stackInput = 1,
                stackOutput = (byte)(index % 2 == 0 ? 1 : 4),
                pickTarget = index + 1,
                insertTarget = size - index,
                pickTargetTypedId = (uint)(0x1000000 | (index + 1)),
                insertTargetTypedId = (uint)(0x1000000 | (size - index)),
            };
        var times = new List<double>();
        long visits = graph.TopologyVisits;
        for (int tick = 0; tick < ticks; tick++)
        {
            var watch = Stopwatch.StartNew();
            for (int index2 = 0; index2 < arms; index2++)
                inserters[index2].InternalUpdate(factory, needs, animation, 1);
            watch.Stop();
            times.Add(watch.Elapsed.TotalMilliseconds);
            long held = 0;
            long heldInc = 0;
            foreach (var arm in inserters)
            {
                held += arm.itemCount;
                heldInc += arm.itemInc;
            }
            var view = graph.View(1);
            if (held + view.Count != arms * 2 || heldInc + view.ProliferatorPoints != arms * 8)
                throw new Exception("inserter tick conservation failed tick=" + tick);
        }
        Check(
            graph.TopologyVisits == visits
                && graph.View(1).Count == arms * 2
                && inserters.All(x => x.itemCount == 0),
            "10000 native inserters preserve all stock and points over 240 ticks"
        );
        times.Sort();
        results.Add(
            "BENCH native_inserters=10000 pipe_nodes=100000 ticks=240 average_ms_tick="
                + times.Average().ToString("F3")
                + " p95_ms_tick="
                + times[(int)(times.Count * 0.95)].ToString("F3")
                + " max_ms_tick="
                + times.Last().ToString("F3")
        );
    }

    private void FluidInserterGeometry()
    {
        foreach (
            var end in new[]
            {
                new Vector3(1.2f, 0, 0),
                new Vector3(3, 0, 0),
                new Vector3(3, 1.3333333f, 0),
                new Vector3(3, -1.3333333f, 0),
                new Vector3(2, 0, 2),
            }
        )
        {
            var mesh = FluidInserterMesh.Create(Vector3.zero, end, Vector3.up);
            try
            {
                var vertices = mesh.vertices;
                var normals = mesh.normals;
                Check(
                    mesh.subMeshCount == 4
                        && vertices.All(position =>
                            !float.IsNaN(position.x)
                            && !float.IsNaN(position.y)
                            && !float.IsNaN(position.z)
                        )
                        && normals.All(normal =>
                            normal.sqrMagnitude > 0.9f && normal.sqrMagnitude < 1.1f
                        ),
                    "finite raised inserter geometry " + end
                );
                Check(
                    mesh.bounds.max.y >= Math.Max(0, end.y) + FluidInserterMesh.Clearance,
                    "inserter trunk clears both connection heights " + end
                );
                Check(
                    mesh.bounds.max.y <= Math.Max(0, end.y) + FluidInserterMesh.Clearance + 0.161f,
                    "inserter body stays within its compact height envelope " + end
                );
                for (int material = 0; material < 4; material++)
                {
                    var triangles = mesh.GetTriangles(material);
                    for (int index = 0; index < triangles.Length; index += 3)
                        if (
                            Vector3
                                .Cross(
                                    vertices[triangles[index + 1]] - vertices[triangles[index]],
                                    vertices[triangles[index + 2]] - vertices[triangles[index]]
                                )
                                .sqrMagnitude < 1e-14f
                        )
                            throw new Exception("Fluid inserter has a degenerate triangle.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }
        var source = new FluidInserterMesh.Socket
        {
            Clamp = true,
            Center = Vector3.zero,
            Axis = Vector3.right,
            Normal = Vector3.up,
            Position = Vector3.up * 0.42f,
        };
        var saddle = FluidInserterMesh.Create(
            source,
            new FluidInserterMesh.Socket { Position = new Vector3(0, 0.42f, 3) },
            Vector3.up
        );
        try
        {
            Check(
                saddle.vertices.All(position2 =>
                    new Vector2(position2.y, position2.z).magnitude >= PipeMesh.Radius - 0.0001f
                ),
                "external saddle and connector vertices remain outside the main pipe bore"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(saddle);
        }
        var machine = new FluidInserterMesh.Socket
        {
            Machine = true,
            Center = Vector3.zero,
            Normal = Vector3.forward,
            Position = new Vector3(0, 0.20f, 0.40f),
        };
        var machineMesh = FluidInserterMesh.Create(
            machine,
            new FluidInserterMesh.Socket { Position = new Vector3(0, 0.2f, 3) },
            Vector3.up
        );
        try
        {
            Check(
                machineMesh.vertices.All(position3 => position3.z >= -0.041f),
                "machine flange seal enters its mounting face by at most 0.04 metres"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(machineMesh);
        }
    }

    private int OrdinaryBelt(PlanetFactory factory, int entity)
    {
        AddObject(factory, entity, 2001);
        var traffic = factory.cargoTraffic;
        int belt = traffic.NewBeltComponent(entity, 1);
        var path = traffic.NewCargoPath();
        path.AddRelatedBelt(belt);
        path.AddBuffer(
            100,
            1,
            new Vector3[100],
            Enumerable.Repeat(Quaternion.identity, 100).ToArray(),
            Vector3.zero
        );
        traffic.beltPool[belt].SetSegment(path.id, 0, 40, 100);
        return belt;
    }

    private void MixedPorts()
    {
        foreach (int proto in new[] { 2103, 2104, 2106 })
        foreach (bool pipeFirst in new[] { false, true })
        {
            var factory = Fixture(2, 930000 + proto * 2 + (pipeFirst ? 1 : 0), true);
            AddObject(factory, 3, proto);
            OrdinaryBelt(factory, 4);
            int first = pipeFirst ? 1 : 4;
            int second = pipeFirst ? 4 : 1;
            factory.WriteObjectConn(3, 0, true, first, 1);
            factory.WriteObjectConn(3, 1, true, second, 1);
            factory.ReadObjectConn(3, 0, out _, out int a, out _);
            factory.ReadObjectConn(3, 1, out _, out int b, out _);
            Check(
                a == first && b == second,
                "mixed ports connect in either order proto=" + proto + " pipeFirst=" + pipeFirst
            );
            factory.WriteObjectConn(4, 2, true, 2, 1);
            factory.ReadObjectConn(4, 2, out _, out int illegal, out _);
            Check(illegal == 0, "ordinary belt cannot directly connect to pipe proto=" + proto);
        }
        var factory2 = Fixture(2, 930001, true);
        int solid = OrdinaryBelt(factory2, 4);
        var traffic = factory2.cargoTraffic;
        var station = new StationComponent
        {
            storage = new[]
            {
                new StationStore
                {
                    itemId = 1000,
                    count = 8,
                    inc = 32,
                    max = 10000,
                },
                new StationStore
                {
                    itemId = 1101,
                    count = 8,
                    inc = 32,
                    max = 10000,
                },
            },
            slots = new[]
            {
                new SlotData
                {
                    beltId = 1,
                    dir = IODir.Output,
                    storageIdx = 1,
                },
                new SlotData
                {
                    beltId = solid,
                    dir = IODir.Output,
                    storageIdx = 2,
                },
            },
            needs = new int[6],
        };
        station.UpdateOutputSlots(traffic, null, 4, false);
        Check(
            station.storage[0].count == 8
                && station.storage[1].count == 4
                && PipeRuntime.Bind(traffic).View(1).Count == 0,
            "shared fluid ports leave ordinary station belt output active"
        );
        var path = traffic.GetCargoPath(traffic.beltPool[solid].segPathId);
        Check(
            path.TryPickItem(0, 100, 1101, out byte stack, out byte proliferatorPoints) == 1101
                && stack == 4
                && proliferatorPoints == 16,
            "ordinary station output retains physical four-stack cargo"
        );
        Check(path.TryInsertItemAtHead(1101, 4, 16, 90), "supply ordinary station input");
        station.needs[0] = 1000;
        station.needs[1] = 1101;
        station.slots = new[]
        {
            new SlotData { beltId = 2, dir = IODir.Input },
            new SlotData { beltId = solid, dir = IODir.Input },
        };
        station.UpdateInputSlots(traffic, null, false);
        Check(
            station.storage[0].count == 8 && station.storage[1].count == 8,
            "shared fluid ports leave ordinary station belt input active"
        );
        Check(
            station.storage.Sum(storageSlot => storageSlot.count)
                + PipeRuntime.Bind(traffic).View(1).Count
                == 16
                && station.storage.Sum(storageSlot2 => storageSlot2.inc)
                    + PipeRuntime.Bind(traffic).View(1).ProliferatorPoints
                    == 64,
            "mixed station transfers conserve count and proliferation"
        );
    }

    private void Undirected()
    {
        var factory = Fixture(18, 940001, false);
        var traffic = factory.cargoTraffic;
        factory.entityPool[1].pos = new Vector3(0, 200, 0);
        for (int index = 2; index <= 14; index++)
        {
            float angle = (index - 2) * Mathf.PI * 2 / 13;
            factory.entityPool[index].pos = new Vector3(
                Mathf.Cos(angle) * 2,
                200 + Mathf.Sin(angle * 2) * 0.5f,
                Mathf.Sin(angle) * 2
            );
        }
        for (int index2 = 1; index2 <= 18; index2++)
            traffic.AlterBeltConnections(index2, 0, 0, 0, 0);
        for (int index3 = 2; index3 <= 14; index3++)
            factory.WriteObjectConn(index3, 0, true, 1, 1);
        var graph = PipeRuntime.Bind(traffic);
        Check(
            PipeTopology.Neighbors(factory, 1).Length == 13 && graph.View(1).Nodes == 14,
            "13 arbitrary-angle interfaces exceed native port limits"
        );
        Check(
            traffic.TryInsertItem(14, 0, 1000, 4, 16)
                && traffic.TryPickItem(2, 0, 1000, out _, out _) == 1000,
            "arbitrary-angle interfaces exchange fluid"
        );
        factory.WriteObjectConn(1, 0, true, 2, 1);
        Check(
            PipeTopology.Neighbors(factory, 1).Length == 13 && graph.View(1).Count == 3,
            "reverse connection is idempotent and preserves existing edges"
        );
        graph.Insert(15, 1007, 1, 0);
        factory.WriteObjectConn(15, 0, true, 1, 1);
        Check(
            graph.View(15).Nodes == 1 && PipeTopology.Neighbors(factory, 1).Length == 13,
            "unlimited junction rejects incompatible fluids"
        );
        factory.prebuildPool[1] = new PrebuildData { id = 1, protoId = (short)Prototypes.ItemId };
        factory.WriteObjectConn(-1, 0, true, 1, 1);
        PipeTopology.Move(factory, -1, 16);
        PipeConnections.Reconcile(factory, 16);
        Check(
            PipeTopology.Neighbors(factory, 16).Contains(1) && graph.View(16).Nodes == 15,
            "prebuild completion retains extra junction links"
        );
        using (var stream = new MemoryStream())
        {
            PipeRuntime.Export(new BinaryWriter(stream));
            stream.Position = 0;
            PipeRuntime.Import(new BinaryReader(stream));
            graph = PipeRuntime.Bind(traffic);
            Check(
                PipeTopology.Neighbors(factory, 1).Length == 14
                    && graph.View(1).Nodes == 15
                    && graph.View(1).Count == 3,
                "unlimited interfaces survive mod save restoration"
            );
        }
        var tool = new BuildTool_Path
        {
            factory = factory,
            handItem = LDB.items.Select(Prototypes.ItemId),
            startObjectId = 1,
        };
        var preview = new BuildPreview();
        PipeBuildConditions.SetCondition(preview, EBuildCondition.Occupied, tool);
        Check(
            preview.condition == EBuildCondition.Ok,
            "pipe preview accepts branching beyond native output capacity"
        );
        PipeBuildConditions.SetCondition(preview, EBuildCondition.InputConflict, tool);
        Check(
            preview.condition == EBuildCondition.Ok,
            "pipe preview removes input direction conflicts"
        );
        PipeBuildConditions.SetCondition(preview, EBuildCondition.TooBend, tool);
        Check(preview.condition == EBuildCondition.Ok, "pipe junction accepts acute angles");
        PipeBuildConditions.SetCondition(preview, EBuildCondition.JointCannotLift, tool);
        Check(preview.condition == EBuildCondition.Ok, "pipe junction permits an elevated branch");
        PipeBuildConditions.SetCondition(preview, EBuildCondition.Collide, tool);
        Check(
            preview.condition == EBuildCondition.Collide,
            "pipe previews retain physical collision rejection"
        );
        tool.handItem = LDB.items.Select(2001);
        PipeBuildConditions.SetCondition(preview, EBuildCondition.InputConflict, tool);
        Check(
            preview.condition == EBuildCondition.InputConflict,
            "ordinary belt direction checks remain active"
        );
    }

    private void PrebuildMigration()
    {
        var factory = Fixture(8, 945001, false);
        factory.prebuildPool[1] = new PrebuildData
        {
            id = 1,
            protoId = (short)Prototypes.ItemId,
            pos = factory.entityPool[5].pos,
        };
        factory.WriteObjectConn(-1, 0, true, 1, 1);
        factory.WriteObjectConn(-1, 1, false, 2, 0);
        factory.HandleObjectConnChangeWhenBuild(-1, 5);
        PipeConnections.Reconcile(factory, 5);
        for (int entityId = 1; entityId <= 8; entityId++)
        for (int slot = 0; slot < 4; slot++)
        {
            factory.ReadObjectConn(entityId, slot, out _, out int peer, out int peerSlot);
            Check(
                peer != -1,
                "completed prebuild identifier leaves native slot " + entityId + ":" + slot
            );
            if (peer == 0)
                continue;
            factory.ReadObjectConn(peer, peerSlot, out _, out int back, out int backSlot);
            Check(
                back == entityId && backSlot == slot,
                "completed prebuild connections remain reciprocal " + entityId + ":" + slot
            );
        }
        factory.prebuildPool[1] = new PrebuildData
        {
            id = 1,
            protoId = (short)Prototypes.ItemId,
            pos = factory.entityPool[8].pos,
        };
        PipeRuntime.UseObjectLinks(factory.cargoTraffic, 1);
        PipeRuntime.Sync(factory.cargoTraffic, 1);
        Check(
            !PipeTopology.Neighbors(factory, 1).Contains(-1),
            "recycled pending identifier creates no remote connection"
        );
    }

    private void LegacyConnections()
    {
        var factory = Fixture(5, 945002, false);
        var graph = PipeRuntime.Bind(factory.cargoTraffic);
        PipeConnections.Connect(factory, 1, 2, 0, 1);
        PipeConnections.Connect(factory, 2, 3, 0, 1);
        PipeTopology.Connect(factory, 1, 5);
        graph.Connect(1, 5);
        graph.Insert(1, 1000, 200, 800);
        factory.entityConnPool[1 * 16 + 3] = -1;
        PipeConnections.RepairImported(factory, true);
        factory.ReadObjectConn(1, 3, out _, out int stale, out _);
        Check(
            stale == 0 && !PipeTopology.Neighbors(factory, 1).Contains(5),
            "legacy restoration removes stale pending slots and unsupported remote edges"
        );
        Check(
            graph.View(1).Count + graph.View(5).Count == 200
                && graph.View(1).ProliferatorPoints + graph.View(5).ProliferatorPoints == 800
                && graph.View(1).Nodes + graph.View(5).Nodes == 4,
            "legacy restoration preserves all nodes, stock and points"
        );
        PipeConnections.Connect(factory, 1, 5, 0, 1);
        PipeConnections.RepairImported(factory, true);
        Check(
            PipeTopology.Neighbors(factory, 1).Contains(5),
            "legacy restoration retains reciprocal native links at arbitrary angles"
        );
    }

    private void ConnectionCuts()
    {
        var factory = Fixture(4, 940002, false);
        var traffic = factory.cargoTraffic;
        for (int index = 1; index < 4; index++)
            factory.WriteObjectConn(index, 0, true, index + 1, 1);
        var graph = PipeRuntime.Bind(traffic);
        graph.Insert(1, 1000, 120, 360);
        Check(PipeConnections.Disconnect(factory, 2, 3), "cut a native-backed pipe edge");
        Check(
            graph.View(1).Nodes == 2
                && graph.View(4).Nodes == 2
                && graph.View(1).Count == 60
                && graph.View(4).Count == 60
                && graph.View(1).ProliferatorPoints + graph.View(4).ProliferatorPoints == 360,
            "cut partitions inventory by capacity and conserves points"
        );
        Check(
            factory.entityPool[2].id == 2
                && factory.entityPool[3].id == 3
                && graph.View(1).Capacity + graph.View(4).Capacity == 400,
            "cut preserves both entities and total capacity"
        );
        for (int slot = 0; slot < 4; slot++)
        {
            factory.ReadObjectConn(2, slot, out _, out int peer, out _);
            Check(peer != 3, "cut clears native connection slot " + slot);
        }
        Check(!PipeConnections.Disconnect(factory, 3, 2), "repeated reverse cut is idempotent");
        factory.WriteObjectConn(2, 0, true, 3, 1);
        factory.WriteObjectConn(4, 0, true, 1, 1);
        Check(
            PipeConnections.Disconnect(factory, 2, 3)
                && graph.View(1).Nodes == 4
                && graph.View(1).Count == 120,
            "cutting a ring edge preserves alternate connectivity"
        );
        Check(
            PipeConnections.Disconnect(factory, 1, 4)
                && graph.View(1).Nodes == 2
                && graph.View(4).Nodes == 2,
            "cutting the remaining bridge splits a ring"
        );
        factory = Fixture(14, 940003, false);
        traffic = factory.cargoTraffic;
        for (int index2 = 2; index2 <= 14; index2++)
            factory.WriteObjectConn(index2, 0, true, 1, 1);
        graph = PipeRuntime.Bind(traffic);
        graph.Insert(1, 1000, 140, 420);
        Check(
            PipeConnections.Disconnect(factory, 1, 14)
                && graph.View(14).Nodes == 1
                && graph.View(1).Nodes == 13,
            "cut an additional edge beyond native slots"
        );
        Check(
            graph.View(14).Count + graph.View(1).Count == 140
                && graph.View(14).ProliferatorPoints + graph.View(1).ProliferatorPoints == 420,
            "additional edge cut conserves fluid and points"
        );
        using (var stream = new MemoryStream())
        {
            PipeRuntime.Export(new BinaryWriter(stream));
            stream.Position = 0;
            PipeRuntime.Import(new BinaryReader(stream));
            graph = PipeRuntime.Bind(traffic);
            Check(
                !PipeTopology.Neighbors(factory, 1).Contains(14) && graph.View(14).Nodes == 1,
                "cut edge remains disconnected after restoration"
            );
        }
    }

    private void Geometry()
    {
        foreach (int shape in new[] { 0, 1, 2, 3, 13, 100, -3 })
        {
            int degree = Math.Abs(shape);
            string label = shape < 0 ? "convex corner" : "degree=" + degree;
            var builder = new PipeMesh();
            var ends = new List<Vector3>();
            var collars = new List<bool>();
            for (int index = 0; index < degree; index++)
            {
                float angle = index * Mathf.PI * 2 / Math.Max(1, degree);
                ends.Add(new Vector3(Mathf.Cos(angle), index % 2 * 0.4f, Mathf.Sin(angle)));
                collars.Add(true);
            }
            if (shape < 0)
            {
                ends.Clear();
                ends.AddRange(new[] { Vector3.right, Vector3.up, Vector3.forward });
            }
            builder.Node(Vector3.zero, Vector3.up, Vector3.forward, ends, collars);
            var mesh = builder.Build("pipe-geometry-check");
            try
            {
                Check(
                    mesh.vertexCount > 0
                        && mesh.subMeshCount == 2
                        && mesh.vertices.All(position =>
                            !float.IsNaN(position.x) && !float.IsInfinity(position.y)
                        ),
                    "finite pipe geometry " + label
                );
                var vertices = mesh.vertices;
                var normals = mesh.normals;
                int tested = 0;
                foreach (var face in new[] { mesh.GetTriangles(0), mesh.GetTriangles(1) })
                    for (int index2 = 0; index2 < face.Length; index2 += 3)
                    {
                        int a = face[index2];
                        int b = face[index2 + 1];
                        int c = face[index2 + 2];
                        var normal = Vector3.Cross(
                            vertices[b] - vertices[a],
                            vertices[c] - vertices[a]
                        );
                        if (normal.sqrMagnitude < 0.0000001f)
                            continue;
                        if (Vector3.Dot(normal, normals[a] + normals[b] + normals[c]) < -0.000001f)
                            throw new Exception("inverted pipe triangle " + label);
                        tested++;
                    }
                Check(tested > 0, "outward pipe faces " + label);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }
        Check(
            PipeVisuals.BodyMaterial != null,
            "procedural pipe material is available in the game"
        );
        var straightBuilder = new PipeMesh();
        straightBuilder.Node(
            Vector3.zero,
            Vector3.up,
            Vector3.forward,
            new[] { Vector3.left, Vector3.right },
            new[] { true, true }
        );
        var straight = straightBuilder.Build("straight-check");
        Check(straight.GetTriangles(1).Length == 0, "straight pipe contains no coupling rings");
        Check(
            straight.vertices.Any(position2 =>
                Mathf.Abs(new Vector2(position2.y, position2.z).magnitude - PipeMesh.Radius * 0.92f)
                < 0.00001f
            ),
            "transparent tubes include a modeled inner glass wall"
        );
        UnityEngine.Object.DestroyImmediate(straight);
        var water = PipeVisuals.LiquidMaterial(1000);
        var acid = PipeVisuals.LiquidMaterial(1116);
        Check(
            water.mainTexture != acid.mainTexture
                && water == PipeVisuals.LiquidMaterial(1000)
                && water.renderQueue < PipeVisuals.BodyMaterial.renderQueue,
            "fluid tint maps remain distinct, cached and behind the transparent wall"
        );
        PreviewGeometry();
    }

    private void Selection()
    {
        var factory = Fixture(2, 950003, false);
        var traffic = factory.cargoTraffic;
        for (int entityId = 1; entityId <= 2; entityId++)
        {
            Check(
                traffic.beltPool[entityId].modelBatchIndex == 0,
                "pipe has no native render batch " + entityId
            );
            traffic.SetBeltSelected(entityId);
            traffic.SetBeltState(entityId, 0);
            traffic.SetBeltState(entityId, -1);
        }
        Check(true, "native selection and build-state updates accept procedural pipes");
    }

    private void MetalOcclusion()
    {
        var cameraObject = new GameObject("metal-occlusion-camera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.enabled = false;
        var opaque = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        var transparent = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        var target = new RenderTexture(128, 128, 24);
        var image = new Texture2D(128, 128, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        try
        {
            opaque.layer = transparent.layer = 29;
            opaque.GetComponent<Renderer>().sharedMaterial = PipeVisuals.JointMaterial;
            transparent.GetComponent<Renderer>().sharedMaterial = PipeVisuals.BodyMaterial;
            camera.orthographic = true;
            camera.orthographicSize = 1;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.cullingMask = 1 << 29;
            camera.transform.position = new Vector3(0, 0, -5);
            camera.transform.rotation = Quaternion.identity;
            camera.targetTexture = target;
            Func<Color> sample = () =>
            {
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 128, 128), 0, 0);
                image.Apply();
                return image.GetPixel(64, 64);
            };
            Func<Color, Color, float> difference = (a, b) =>
                Mathf.Max(
                    Mathf.Abs(a.r - b.r),
                    Mathf.Max(Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b))
                );
            transparent.SetActive(false);
            camera.backgroundColor = Color.black;
            var metal = sample();
            camera.backgroundColor = Color.magenta;
            var otherBackground = sample();
            Check(
                difference(metal, otherBackground) < 0.02f,
                "metal surface blocks the background " + metal + " / " + otherBackground
            );
            transparent.SetActive(true);
            transparent.transform.position = Vector3.forward;
            camera.backgroundColor = Color.black;
            var behind = sample();
            Check(
                difference(metal, behind) < 0.02f,
                "glass behind metal fails the depth test " + metal + " / " + behind
            );
            transparent.transform.position = Vector3.back;
            var front = sample();
            camera.backgroundColor = Color.magenta;
            var frontOther = sample();
            Check(
                difference(front, frontOther) < 0.02f,
                "foreground glass preserves metal opacity " + front + " / " + frontOther
            );

            transparent.SetActive(false);
            var builder = new PipeMesh();
            builder.Node(
                Vector3.zero,
                Vector3.forward,
                Vector3.right,
                new[] { Vector3.left, Vector3.up },
                new[] { true, true }
            );
            builder.Node(
                Vector3.left * 2,
                Vector3.forward,
                Vector3.right,
                new[] { Vector3.left * 3, Vector3.left },
                new[] { false, false }
            );
            builder.Node(
                Vector3.up * 2,
                Vector3.forward,
                Vector3.up,
                new[] { Vector3.up * 3, Vector3.up },
                new[] { false, false }
            );
            var elbow = builder.Build("metal-interior-check");
            var filter = opaque.GetComponent<MeshFilter>();
            var original = filter.sharedMesh;
            filter.sharedMesh = elbow;
            opaque.GetComponent<Renderer>().sharedMaterials = new[]
            {
                PipeVisuals.BodyMaterial,
                PipeVisuals.JointMaterial,
            };
            camera.transform.SetPositionAndRotation(
                Vector3.left * 2,
                Quaternion.LookRotation(Vector3.right, Vector3.forward)
            );
            try
            {
                foreach (var axis in new[] { Vector3.left, Vector3.up })
                {
                    camera.transform.SetPositionAndRotation(
                        axis * 2,
                        Quaternion.LookRotation(-axis, Vector3.forward)
                    );
                    camera.backgroundColor = Color.black;
                    var inside = sample();
                    camera.backgroundColor = Color.magenta;
                    var insideOther = sample();
                    Check(
                        difference(inside, insideOther) < 0.02f,
                        "elbow interior blocks the background from branch "
                            + axis
                            + " "
                            + inside
                            + " / "
                            + insideOther
                    );
                }
            }
            finally
            {
                filter.sharedMesh = original;
                UnityEngine.Object.DestroyImmediate(elbow);
            }

            var cornerBuilder = new PipeMesh();
            var cornerAxes = new[] { Vector3.right, Vector3.up, Vector3.forward };
            cornerBuilder.Node(
                Vector3.zero,
                Vector3.up,
                Vector3.forward,
                cornerAxes,
                new[] { true, true, true }
            );
            foreach (var axis in cornerAxes)
                cornerBuilder.Node(
                    axis * 2,
                    Vector3.up,
                    axis,
                    new[] { axis, axis * 3 },
                    new[] { false, false }
                );
            var corner = cornerBuilder.Build("corner-interior-check");
            filter.sharedMesh = corner;
            try
            {
                foreach (var axis in cornerAxes)
                {
                    camera.transform.SetPositionAndRotation(
                        axis * 2,
                        Quaternion.LookRotation(
                            -axis,
                            axis == Vector3.up ? Vector3.forward : Vector3.up
                        )
                    );
                    camera.backgroundColor = Color.black;
                    var inside = sample();
                    camera.backgroundColor = Color.magenta;
                    var insideOther = sample();
                    Check(
                        difference(inside, insideOther) < 0.02f,
                        "convex junction interior blocks the background from branch "
                            + axis
                            + " "
                            + inside
                            + " / "
                            + insideOther
                    );
                }
            }
            finally
            {
                filter.sharedMesh = original;
                UnityEngine.Object.DestroyImmediate(corner);
            }
        }
        finally
        {
            RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(cameraObject);
            UnityEngine.Object.DestroyImmediate(opaque);
            UnityEngine.Object.DestroyImmediate(transparent);
            UnityEngine.Object.DestroyImmediate(image);
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
        }
    }

    private void RenderCache()
    {
        var factory = Fixture(3, 950002, false);
        var origin = new Vector3(0, 200, 0);
        factory.entityPool[1].pos = origin + Vector3.left * 1.2f;
        factory.entityPool[2].pos = origin;
        factory.entityPool[3].pos = origin + Vector3.right * 1.2f;
        PipeConnections.Connect(factory, 1, 2, 0, 1);
        PipeConnections.Connect(factory, 2, 3, 0, 1);
        var cameraObject = new GameObject("liquid-cache-camera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.enabled = false;
        camera.transform.position = origin + new Vector3(0, 4, -6);
        camera.transform.LookAt(origin);
        try
        {
            int liquidBefore = PipeVisuals.LiquidRebuilds;
            Check(PipeVisuals.Draw(factory, camera) > 0, "live chunk renderer draws pipe geometry");
            int rebuilds = PipeVisuals.Rebuilds;
            Check(
                PipeVisuals.LiquidRebuilds == liquidBefore,
                "empty pipe networks generate no liquid geometry"
            );
            int nodeBuilds = PipeVisuals.NodeRebuilds;
            PipeVisuals.Dirty(factory, 1);
            PipeVisuals.Draw(factory, camera);
            Check(
                PipeVisuals.NodeRebuilds == nodeBuilds && PipeVisuals.Rebuilds == rebuilds,
                "unchanged native notifications preserve cached node and chunk geometry"
            );
            var graph = PipeRuntime.Bind(factory.cargoTraffic);
            graph.Insert(1, 1000, 100, 0);
            PipeVisuals.Draw(factory, camera);
            graph.Take(3, 100);
            graph.Insert(1, 1116, 100, 0);
            PipeVisuals.Draw(factory, camera);
            Check(
                PipeVisuals.Rebuilds == rebuilds,
                "fluid presence and type changes do not rebuild pipe geometry"
            );
            PipeConnections.Disconnect(factory, 1, 2);
            PipeVisuals.Draw(factory, camera);
            Check(
                PipeVisuals.NodeRebuilds - nodeBuilds == 2,
                "cutting an edge remeshes only its two endpoints"
            );
        }
        finally
        {
            PipeVisuals.Clear();
            UnityEngine.Object.DestroyImmediate(cameraObject);
        }
    }

    private void PreviewGeometry()
    {
        var fixture = Fixture(13, 950001, false);
        var positions = new[]
        {
            new Vector3(-5, 0, 0),
            new Vector3(-3.8f, 0, 0),
            new Vector3(-2.6f, 0, 0),
            new Vector3(-1.4f, 0, 0),
            new Vector3(-0.2f, 0, 0),
            new Vector3(1, 0, 0),
            new Vector3(1, 0, 1.2f),
            new Vector3(1, 0, 2.4f),
            new Vector3(2.2f, 0, 2.4f),
            new Vector3(3.4f, 0.7f, 2.4f),
            new Vector3(4.6f, 1.4f, 2.4f),
            new Vector3(-1.4f, 0, -1.2f),
            new Vector3(-1.4f, 0, -2.4f),
        };
        var origin = new Vector3(0, 200, 0);
        for (int index = 0; index < positions.Length; index++)
            fixture.entityPool[index + 1].pos = positions[index] + origin;
        for (int index2 = 1; index2 < 11; index2++)
            PipeConnections.Connect(fixture, index2, index2 + 1, 0, 1);
        PipeConnections.Connect(fixture, 4, 12, 0, 1);
        PipeConnections.Connect(fixture, 12, 13, 0, 1);
        var builder = new PipeMesh();
        for (int index3 = 1; index3 <= 13; index3++)
            PipeVisuals.AddEntity(builder, fixture, index3, origin);
        var mesh = builder.Build("FluidPipes.RenderPreview");
        RenderPreview(mesh, Path.Combine(Path.GetDirectoryName(Info.Location), "pipe-preview.png"));
        UnityEngine.Object.DestroyImmediate(mesh);
    }

    private static void RenderPreview(Mesh mesh, string path)
    {
        var cameraObject = new GameObject("pipe-preview-camera");
        var lightObject = new GameObject("pipe-preview-light");
        var pipeObject = new GameObject("pipe-preview-model");
        var target = new RenderTexture(1280, 720, 24);
        var previous = RenderTexture.active;
        var ambient = RenderSettings.ambientLight;
        try
        {
            var filter = pipeObject.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = pipeObject.AddComponent<MeshRenderer>();
            var steel = PipeVisuals.BodyMaterial;
            var brass = new Material(steel) { color = new Color(0.72f, 0.48f, 0.2f) };
            renderer.sharedMaterials = new[] { steel, brass };
            pipeObject.layer = 30;
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.4f;
            light.cullingMask = 1 << 30;
            light.transform.rotation = Quaternion.Euler(45, -25, 0);
            RenderSettings.ambientLight = new Color(0.6f, 0.6f, 0.6f);
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 4.4f;
            camera.transform.position = new Vector3(5, 8, -10);
            camera.transform.LookAt(new Vector3(0, 0, 0.4f));
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.035f, 0.05f, 0.065f);
            camera.cullingMask = 1 << 30;
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(brass);
        }
        finally
        {
            RenderTexture.active = previous;
            RenderSettings.ambientLight = ambient;
            UnityEngine.Object.DestroyImmediate(cameraObject);
            UnityEngine.Object.DestroyImmediate(lightObject);
            UnityEngine.Object.DestroyImmediate(pipeObject);
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
