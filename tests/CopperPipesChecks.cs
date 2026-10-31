using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Bootstrap;
using FluidPipes;
using FluidPipes.API;
using HarmonyLib;
using UnityEngine;

public sealed partial class RuntimeChecks
{
    private const string CopperConsumerGuid = "example.copperpipes";

    private void StandalonePipeLibrary()
    {
        Check(
            !Chainloader.PluginInfos.ContainsKey(CopperConsumerGuid)
                && !PipeTypes.TryGet(CopperConsumerGuid + ":copper", out _)
                && PipeTypes.TryGet(PipeApi.PipeItemId, out _)
                && LDB.items.Select(PipeApi.ValveItemId) != null,
            "standalone FluidPipes initializes its default pipe and valve with external consumers absent"
        );
    }

    private void CopperPipesConsumer()
    {
        Check(
            Chainloader.PluginInfos.TryGetValue(CopperConsumerGuid, out var consumer)
                && consumer.Instance != null
                && consumer.Instance.GetType().Assembly.GetName().Name == "CopperPipes"
                && consumer.Instance.GetType().Assembly != typeof(RuntimeChecks).Assembly
                && consumer.Instance.GetType().Assembly != typeof(PipeTypes).Assembly,
            "CopperPipes loads as an independent external BepInEx consumer assembly"
        );
        Check(
            consumer.Metadata.Version.Equals(new System.Version(1, 1, 0))
                && consumer
                    .Instance.GetType()
                    .GetCustomAttributes(typeof(BepInDependency), true)
                    .Cast<BepInDependency>()
                    .Any(dependency =>
                        dependency.DependencyGUID == PipeApi.PluginGuid
                        && dependency.MinimumVersion.Equals(new System.Version(PipeApi.ModVersion))
                    ),
            "the copper consumer declares its compiled FluidPipes dependency version"
        );
        Check(
            PipeTypes.TryGet(CopperConsumerGuid + ":copper", out var copper)
                && copper.OwnerGuid == CopperConsumerGuid
                && copper.Behavior == PipeBehavior.SharedInventory
                && copper.DirectionMode == PipeDirectionMode.Undirected
                && PipeTypes.TryGet(PipeApi.PipeItemId, out var glass)
                && copper.ItemId != glass.ItemId
                && copper.ConnectionFamily == glass.ConnectionFamily,
            "external copper registration preserves its independent identity and the default fluid family"
        );
        var item = LDB.items.Select(copper.ItemId);
        var recipe = LDB.recipes.dataArray.Single(candidate => candidate.Results.Contains(item.ID));
        var beltRecipe = LDB.recipes.dataArray.First(candidate => candidate.Results.Contains(2001));
        var tankRecipe = LDB.recipes.dataArray.First(candidate => candidate.Results.Contains(2106));
        var technology = LDB.techs.dataArray.First(candidate =>
            candidate.UnlockRecipes != null && candidate.UnlockRecipes.Contains(tankRecipe.ID)
        );
        Check(
            item.Name == "CopperPipes.Name"
                && item.Description == "CopperPipes.Description"
                && item.prefabDesc.isBelt
                && recipe.ID != Prototypes.RecipeId
                && recipe.Items.SequenceEqual(beltRecipe.Items)
                && recipe.ItemCounts.SequenceEqual(beltRecipe.ItemCounts)
                && recipe.TimeSpend == beltRecipe.TimeSpend
                && recipe.ResultCounts[Array.IndexOf(recipe.Results, item.ID)]
                    == beltRecipe.ResultCounts[Array.IndexOf(beltRecipe.Results, 2001)]
                && item.PreTechOverride == technology.ID
                && technology.UnlockRecipes.Contains(recipe.ID),
            "external copper prototypes retain their own name and recipe with belt crafting and tank technology"
        );
        Check(
            item.GridIndex > 0
                && !LDB.items.dataArray.Any(candidate =>
                    candidate.ID != item.ID && candidate.GridIndex == item.GridIndex
                )
                && !LDB.recipes.dataArray.Any(candidate =>
                    candidate.ID != recipe.ID && candidate.GridIndex == recipe.GridIndex
                )
                && PipeApi.PipeItemId == Prototypes.ItemId
                && PipeApi.ValveItemId == Prototypes.ValveId,
            "external copper crafting uses a free slot while default pipe and valve identities remain available"
        );
        CopperPipesMaterials(copper.ItemId);
        CopperIconChecks.Verify(copper.ItemId, Check);
        WithTypeNativePaths(() =>
        {
            CopperPipesNativeLifecycle(copper.ItemId);
            CaptureCopperPipesComparison(copper.ItemId);
        });
        PipeVisuals.Clear();
        PipeRuntime.Clear();
    }

    private void CopperPipesMaterials(int copperItemId)
    {
        var defaultMaterials = PipeMaterialLibrary.Get(PipeApi.PipeItemId);
        Color defaultWall = defaultMaterials.Wall.color;
        Color defaultJoint = defaultMaterials.Fittings.color;
        int defaultQueue = defaultMaterials.Wall.renderQueue;
        int defaultDepth = defaultMaterials.Wall.GetInt("_ZWrite");
        var copperMaterials = PipeMaterialLibrary.Get(copperItemId);
        Color expectedCopper = new Color(0.72f, 0.36f, 0.16f);
        Check(
            copperMaterials.Wall != defaultMaterials.Wall
                && copperMaterials.Fittings != defaultMaterials.Fittings
                && copperMaterials.Fittings.color == expectedCopper
                && copperMaterials.Wall.renderQueue == 3000
                && copperMaterials.Wall.GetInt("_ZWrite") == 0
                && copperMaterials.Fittings.renderQueue == 2000
                && copperMaterials.Fittings.GetFloat("_Metallic") > 0.9f
                && copperMaterials.Fittings.GetFloat("_Glossiness") >= 0.7f
                && PipeTypes.TryGet(copperItemId, out var copperType)
                && copperType.Appearance.WallLayout != null,
            "external copper combines glass windows with opaque metallic wall sectors and joints"
        );
        Check(
            defaultMaterials.Wall.color == defaultWall
                && defaultMaterials.Fittings.color == defaultJoint
                && defaultMaterials.Wall.renderQueue == defaultQueue
                && defaultQueue == 3000
                && defaultMaterials.Wall.GetInt("_ZWrite") == defaultDepth
                && defaultDepth == 0,
            "external material initialization preserves the default glass and metal surfaces"
        );
    }

    private void CopperPipesNativeLifecycle(int copperItemId)
    {
        var factory = Fixture(0, 981030, false);
        for (int entityId = 1; entityId <= 6; entityId++)
        {
            AddCopperConsumerNode(
                factory,
                entityId,
                entityId == 3 || entityId == 4 ? PipeApi.PipeItemId : copperItemId,
                new Vector3((entityId - 1) * 1.2f, 200, 0)
            );
        }
        for (int entityId = 1; entityId < 6; entityId++)
        {
            factory.WriteObjectConn(entityId, 0, true, entityId + 1, 1);
        }
        RebuildTypeNativePaths(factory, 6);
        Check(
            PipeTypes.GetConnectedNodes(factory, 1).Length == 6
                && PipeApi.ShareInventory(factory, 1, 6)
                && Enumerable.Range(1, 6).All(entityId => PipeApi.IsPipe(factory, entityId)),
            "native creation and path linking join external copper nodes with default glass nodes"
        );
        Check(
            factory.InsertInto(1, 0, 1000, 4, 12, out _) == 4
                && PipeApi.TryInsert(factory, 1, 1000, 32, 96)
                && factory.cargoTraffic.TryPickItem(6, 0, 1000, out byte stack, out byte points)
                    == 1000
                && stack == 1
                && points == 3
                && PipeApi.TryTake(factory, 6, 5, out var removed)
                && removed.Count == 5
                && removed.ProliferatorPoints == 15
                && PipeApi.TryGetInventory(factory, 3, out var inventory)
                && inventory.Count == 30
                && inventory.ProliferatorPoints == 90,
            "external copper native insertion and remote extraction conserve mixed-network stock and proliferation"
        );
        Check(
            PipeConnections.Disconnect(factory, 3, 4)
                && PipeApi.TryGetInventory(factory, 1, out var left)
                && left.Count == 15
                && PipeApi.TryGetInventory(factory, 6, out var right)
                && right.Count == 15
                && left.ProliferatorPoints + right.ProliferatorPoints == 90,
            "cutting a copper-glass network preserves inventory across both resulting spaces"
        );
        factory.WriteObjectConn(3, 0, true, 4, 1);
        RebuildTypeNativePaths(factory, 6);
        RoundTripTypeState();
        Check(
            PipeApi.TryGetInventory(factory, 6, out inventory)
                && inventory.Count == 30
                && inventory.ProliferatorPoints == 90
                && PipeTypes.GetConnectedNodes(factory, 1).Length == 6,
            "external copper reconnection and save reload preserve shared stock and type topology"
        );
        var refunds = new Harmony("local.fluidpipes.external-consumer-refunds");
        refundCount = refundInc = 0;
        refunds.Patch(
            AccessTools.Method(typeof(PipeRuntime), nameof(PipeRuntime.Return)),
            prefix: new HarmonyMethod(typeof(RuntimeChecks), nameof(RefundPrefix))
        );
        try
        {
            factory.cargoTraffic.RemoveBeltComponent(factory.entityPool[2].beltId);
            Check(
                !PipeApi.IsPipe(factory, 2)
                    && PipeApi.TryGetInventory(factory, 1, out left)
                    && PipeApi.TryGetInventory(factory, 3, out right)
                    && left.Count + right.Count + refundCount == 30
                    && left.ProliferatorPoints + right.ProliferatorPoints + refundInc == 90,
                "native copper dismantling preserves surviving stock and player refunds"
            );
        }
        finally
        {
            refunds.UnpatchSelf();
        }
    }

    private static void AddCopperConsumerNode(
        PlanetFactory factory,
        int entityId,
        int itemId,
        Vector3 position
    )
    {
        AddObject(factory, entityId, itemId);
        factory.entityPool[entityId].pos = position;
        factory.entityPool[entityId].rot = Quaternion.identity;
        factory.cargoTraffic.NewBeltComponent(entityId, 1);
    }

    private void CaptureCopperPipesComparison(int copperItemId)
    {
        var factory = Fixture(0, 981031, false);
        var points = new[]
        {
            new Vector3(-3.6f, 200, 0),
            new Vector3(-2.4f, 200, 0),
            new Vector3(-1.2f, 200, 0),
            new Vector3(0, 200, 0),
            new Vector3(0, 200, 1.2f),
            new Vector3(1.2f, 200, 1.2f),
            new Vector3(2.4f, 200, 1.2f),
        };
        for (int row = 0; row < 2; row++)
        {
            for (int index = 0; index < points.Length; index++)
            {
                int entityId = row * points.Length + index + 1;
                AddCopperConsumerNode(
                    factory,
                    entityId,
                    row == 0 ? copperItemId : PipeApi.PipeItemId,
                    points[index] + Vector3.forward * (row * 3 - 2)
                );
                if (index > 0)
                {
                    factory.WriteObjectConn(entityId - 1, 0, true, entityId, 1);
                }
            }
        }
        RebuildTypeNativePaths(factory, 14);
        Check(
            PipeApi.TryInsert(factory, 1, 1000, 49, 0)
                && PipeApi.TryInsert(factory, 8, 1000, 49, 0),
            "copper and default glass comparison networks accept the same water inventory"
        );
        var objects = new List<GameObject>();
        var resources = new List<UnityEngine.Object>();
        var previousTarget = RenderTexture.active;
        var ambient = RenderSettings.ambientLight;
        var target = new RenderTexture(1600, 1000, 24);
        try
        {
            RenderSettings.ambientLight = new Color(0.65f, 0.65f, 0.65f);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            objects.Add(floor);
            floor.transform.position = new Vector3(-0.6f, 199.88f, 0.1f);
            floor.transform.localScale = new Vector3(11, 0.15f, 8);
            var floorMaterial = new Material(Shader.Find("Standard"))
            {
                color = new Color(0.24f, 0.29f, 0.32f),
            };
            floorMaterial.SetFloat("_Glossiness", 0.25f);
            resources.Add(floorMaterial);
            floor.GetComponent<MeshRenderer>().sharedMaterial = floorMaterial;
            var cameraObject = new GameObject("external-consumer-camera");
            objects.Add(cameraObject);
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = 4.2f;
            camera.aspect = 1.6f;
            camera.transform.position = new Vector3(7, 209, -10);
            camera.transform.LookAt(new Vector3(-0.6f, 200, 0.4f));
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.035f, 0.05f, 0.065f);
            camera.cullingMask = 1;
            camera.targetTexture = target;
            var lightObject = new GameObject("external-consumer-light");
            objects.Add(lightObject);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.4f;
            light.transform.rotation = Quaternion.Euler(45, -30, 0);
            var font = Font.CreateDynamicFontFromOSFont("Arial", 48);
            resources.Add(font);
            AddCopperCaption(
                objects,
                camera,
                font,
                "EXTERNAL COPPER  /  DEFAULT GLASS",
                new Vector2(0.06f, 0.94f),
                0.065f
            );
            AddCopperCaption(
                objects,
                camera,
                font,
                "Same geometry and water capacity  |  independent materials",
                new Vector2(0.06f, 0.885f),
                0.04f
            );
            Check(
                PipeVisuals.Draw(factory, camera) > 0,
                "the live renderer draws external copper and default glass in one scene"
            );
            camera.Render();
            RenderTexture.active = target;
            var image = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
            resources.Add(image);
            image.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0);
            image.Apply();
            string imagePath = Path.Combine(
                Path.GetDirectoryName(Info.Location),
                "pipe-copper-consumer.png"
            );
            File.WriteAllBytes(imagePath, image.EncodeToPNG());
            results.Add("SCREENSHOT " + imagePath);
        }
        finally
        {
            RenderTexture.active = previousTarget;
            RenderSettings.ambientLight = ambient;
            foreach (var gameObject in objects)
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
            foreach (var resource in resources)
            {
                UnityEngine.Object.DestroyImmediate(resource);
            }
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
        }
    }

    private static void AddCopperCaption(
        List<GameObject> objects,
        Camera camera,
        Font font,
        string text,
        Vector2 position,
        float characterSize
    )
    {
        var captionObject = new GameObject("external-consumer-caption");
        objects.Add(captionObject);
        var caption = captionObject.AddComponent<TextMesh>();
        caption.text = text;
        caption.font = font;
        caption.fontSize = 48;
        caption.characterSize = characterSize;
        caption.anchor = TextAnchor.UpperLeft;
        caption.color = new Color(0.86f, 0.91f, 0.95f);
        captionObject.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        captionObject.transform.SetPositionAndRotation(
            camera.ViewportToWorldPoint(new Vector3(position.x, position.y, 2)),
            camera.transform.rotation
        );
    }
}
