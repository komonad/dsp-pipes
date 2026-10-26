using System;
using System.IO;
using System.Linq;
using BepInEx.Bootstrap;
using FluidPipes;
using FluidPipes.API;
using HarmonyLib;

public sealed partial class SceneCapture
{
    private void CheckDisabledContentInitialization()
    {
        RequireCopperInteraction(
            Chainloader.PluginInfos.TryGetValue("local.fluidpipes.content-optout", out var startup)
                && startup.Instance.GetType().Assembly.GetName().Name == "EarlyContentOptOut"
                && Chainloader.PluginInfos.TryGetValue("example.copperpipes", out var consumer)
                && consumer.Instance.GetType().Assembly.GetName().Name == "CopperPipes",
            "library-only startup and copper content load from separate plugin assemblies"
        );
        string bootstrapPath = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(Info.Location),
            "disabled-content-bootstrap.txt"
        );
        RequireCopperInteraction(
            File.Exists(bootstrapPath) && File.ReadAllLines(bootstrapPath).Last() == "Failures=0",
            "the external startup plugin completes public opt-out and database isolation checks"
        );
        report.AddRange(File.ReadAllLines(bootstrapPath).Where(line => line.StartsWith("PASS ")));
        RequireCopperInteraction(
            PipeApi.IsReady
                && PipeApi.PipeItemId == 0
                && PipeApi.ValveItemId == 0
                && PipeApi.PipeRecipeId == 0
                && PipeApi.ValveRecipeId == 0
                && PipeApi.IsItemAllowed(Water)
                && PipeTypes.IsPipeItem(copperInteractionItem),
            "the initialized library admits fluids and resolves external pipes with all built-in IDs zero"
        );
        var buildbar = (ItemProto[,])
            AccessTools.Field(typeof(UIBuildMenu), "protos").GetValue(null);
        RequireCopperInteraction(
            buildbar != null
                && buildbar
                    .Cast<ItemProto>()
                    .All(item =>
                        item == null
                        || item.Name != "FluidPipes.Name" && item.Name != "FluidPipes.Valve"
                    ),
            "the live native build menu contains no built-in pipe or valve entries"
        );
        CopperIconChecks.Verify(copperInteractionItem, RequireCopperInteraction);
    }

    private void CheckDisabledContentCrafting()
    {
        RecipeProto recipe = LDB.recipes.dataArray.Single(candidate =>
            candidate.Results.Contains(copperInteractionItem)
        );
        RecipeProto tankRecipe = LDB.recipes.dataArray.First(candidate =>
            candidate.Results.Contains(2106)
        );
        Player player = GameMain.mainPlayer;
        RequireCopperInteraction(
            GameMain.history.RecipeUnlocked(tankRecipe.ID)
                && GameMain.history.RecipeUnlocked(recipe.ID),
            "the external copper recipe follows the tank unlock in the loaded history"
        );
        for (int index = 0; index < recipe.Items.Length; index++)
        {
            player.package.AddItemStacked(recipe.Items[index], recipe.ItemCounts[index], 0, out _);
        }
        int previousCount = player.package.GetItemCount(copperInteractionItem);
        MechaForge forge = player.mecha.forge;
        RequireCopperInteraction(
            forge.tasks.Count == 0 && forge.AddTask(recipe.ID, 1) != null,
            "the native replicator accepts a copper recipe with its registered ingredients"
        );
        for (int tick = 0; tick < 3600 && forge.tasks.Count > 0; tick++)
        {
            player.mecha.coreEnergy = player.mecha.coreEnergyCap;
            forge.GameTick(tick, 1f / 60);
        }
        int resultCount = recipe.ResultCounts[Array.IndexOf(recipe.Results, copperInteractionItem)];
        RequireCopperInteraction(
            forge.tasks.Count == 0
                && player.package.GetItemCount(copperInteractionItem)
                    == previousCount + resultCount,
            "the native replicator delivers the configured copper crafting batch"
        );
    }

    private void CheckDisabledContentInventory(InteractionPath path)
    {
        int firstNode = path.Entities[0];
        int lastNode = path.Entities[path.Entities.Length - 1];
        int middleNode = path.Entities[path.Entities.Length / 2];
        RequireCopperInteraction(
            PipeApi.TryInsert(factory, firstNode, Water, 20, 80)
                && PipeApi.ShareInventory(factory, firstNode, lastNode)
                && PipeApi.TryTake(factory, lastNode, 5, out var withdrawn)
                && withdrawn.Count == 5
                && withdrawn.ProliferatorPoints == 20
                && PipeApi.TryGetInventory(factory, middleNode, out var inventory)
                && inventory.Count == 15
                && inventory.ProliferatorPoints == 60,
            "external-only native construction stores and extracts a conserved shared water inventory"
        );
        Player player = GameMain.mainPlayer;
        player.position = factory.entityPool[middleNode].pos;
        player.controller.actionInspect.SetInspectee(EObjectType.None, middleNode);
        PipePanel.Update();
        UITankWindow window = PipePanel.InventoryWindow;
        try
        {
            RequireCopperInteraction(
                window != null
                    && window.active
                    && PipeSelectionHighlights.SelectedEntity == middleNode
                    && PipeSelectionHighlights.Count == 3,
                "external pipe inspection opens the native tank window and outlines its adjacent nodes"
            );
            SetUiHand(Water, 10, 40);
            NativeTankGesture(window);
            RequireCopperInteraction(
                player.inhandItemCount == 8
                    && player.inhandItemInc == 32
                    && PipeApi.TryGetInventory(factory, lastNode, out inventory)
                    && inventory.Count == 17
                    && inventory.ProliferatorPoints == 68,
                "native tank pointer injection adds water to an external-only network"
            );
            SetUiHand(0, 0, 0);
            NativeTankGesture(window);
            RequireCopperInteraction(
                player.inhandItemId == Water
                    && player.inhandItemCount == 2
                    && player.inhandItemInc == 8
                    && PipeApi.TryGetInventory(factory, firstNode, out inventory)
                    && inventory.Count == 15
                    && inventory.ProliferatorPoints == 60,
                "native tank pointer extraction withdraws conserved stock from an external-only network"
            );
        }
        finally
        {
            SetUiHand(0, 0, 0);
            player.controller.actionInspect.SetInspectee(EObjectType.None, 0);
            PipePanel.Update();
            player.position = center;
        }
    }
}
