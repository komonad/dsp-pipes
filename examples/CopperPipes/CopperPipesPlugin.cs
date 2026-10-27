using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using CommonAPI;
using CommonAPI.Systems.ModLocalization;
using FluidPipes.API;
using UnityEngine;
using xiaoye97;

namespace CopperPipes
{
    /// <summary>通过 FluidPipes 公共扩展 API 注册可制造的铜制管道。</summary>
    [BepInPlugin(PluginGuid, "Copper Pipes", "1.1.0")]
    [BepInDependency(PipeApi.PluginGuid, "0.9.0")]
    [BepInDependency(CommonAPIPlugin.GUID)]
    [BepInDependency("me.xiaoye97.plugin.Dyson.LDBTool")]
    [CommonAPISubmoduleDependency(nameof(LocalizationModule))]
    public sealed class CopperPipesPlugin : BaseUnityPlugin
    {
        private const string PluginGuid = "example.copperpipes";
        private const int CopperItemId = 11960;
        private const int CopperRecipeId = 11960;
        private ItemProto copperItem;
        private RecipeProto copperRecipe;
        private int tankRecipeId;
        private GameHistoryData synchronizedHistory;

        private void Awake()
        {
            LocalizationModule.RegisterTranslation("CopperPipes.Name", "Copper Pipe", "铜管", "");
            LocalizationModule.RegisterTranslation(
                "CopperPipes.Description",
                "Copper pipe with glass inspection windows. Connected pipes share one inventory. Capacity: 10 per segment.",
                "带玻璃观察窗的铜制管道。连通管道共享库存，每段容量10。",
                ""
            );
            LDBTool.PreAddDataAction += RegisterPrototypes;
            LDBTool.PostAddDataAction += FinishPrototypes;
        }

        private void OnDestroy()
        {
            LDBTool.PreAddDataAction -= RegisterPrototypes;
            LDBTool.PostAddDataAction -= FinishPrototypes;
        }

        private void OnApplicationQuit()
        {
            CopperPipeIcon.Release();
        }

        private void RegisterPrototypes()
        {
            copperItem = PipeTypes.CreateItem(
                itemId: CopperItemId,
                nameKey: "CopperPipes.Name",
                descriptionKey: "CopperPipes.Description"
            );
            PipeTypes.SetItemIcon(prototype: copperItem, icon: CopperPipeIcon.Load());
            copperItem.GridIndex = 0;
            LDBTool.PreAddProto(copperItem);
            RecipeProto beltRecipe = LDB.recipes.dataArray.First(recipe =>
                recipe.Results.Contains(2001)
            );
            copperRecipe = new RecipeProto
            {
                ID = CopperRecipeId,
                Name = copperItem.Name,
                Description = copperItem.Description,
                IconPath = "",
                Type = beltRecipe.Type,
                Handcraft = beltRecipe.Handcraft,
                NonProductive = beltRecipe.NonProductive,
                TimeSpend = beltRecipe.TimeSpend,
                Items = (int[])beltRecipe.Items.Clone(),
                ItemCounts = (int[])beltRecipe.ItemCounts.Clone(),
                Results = new[] { copperItem.ID },
                ResultCounts = new[]
                {
                    beltRecipe.ResultCounts[Array.IndexOf(beltRecipe.Results, 2001)],
                },
            };
            LDBTool.PreAddProto(copperRecipe);
            var appearance = new PipeAppearance(
                wallMode: PipeWallMode.Glass,
                configureMaterials: materials =>
                {
                    ConfigureCopper(materials.Fittings);
                },
                wallLayout: new PipeWallLayout(
                    new[]
                    {
                        new PipeWallBand(centerDegrees: 0, widthDegrees: 90),
                        new PipeWallBand(
                            centerDegrees: 180,
                            widthDegrees: 0,
                            verticalWidthDegrees: 90
                        ),
                    }
                )
            );
            PipeTypes.Register(
                ownerGuid: PluginGuid,
                definition: new PipeTypeDefinition(
                    localName: "copper",
                    prototype: copperItem,
                    appearance: appearance,
                    behavior: PipeBehavior.SharedInventory
                )
            );
        }

        private void FinishPrototypes()
        {
            int gridIndex = FindAvailableGridIndex();
            copperItem.GridIndex = gridIndex;
            copperRecipe.GridIndex = gridIndex;
            tankRecipeId = LDB.recipes.dataArray.First(recipe => recipe.Results.Contains(2106)).ID;
            TechProto unlockTechnology = LDB.techs.dataArray.First(technology =>
                technology.UnlockRecipes != null && technology.UnlockRecipes.Contains(tankRecipeId)
            );
            copperItem.PreTechOverride = unlockTechnology.ID;
            unlockTechnology.UnlockRecipes = unlockTechnology
                .UnlockRecipes.Concat(new[] { copperRecipe.ID })
                .Distinct()
                .ToArray();

            copperItem.handcrafts = copperItem.recipes = copperItem.makes = null;
            copperItem.rawMats = null;
            copperItem.Preload(Array.IndexOf(LDB.items.dataArray, copperItem));
            copperRecipe.Preload(Array.IndexOf(LDB.recipes.dataArray, copperRecipe));
            unlockTechnology.Preload();
            unlockTechnology.Preload2();
            Logger.LogInfo(
                "Copper pipe registered: item="
                    + copperItem.ID
                    + ", recipe="
                    + copperRecipe.ID
                    + ", grid="
                    + gridIndex
                    + ", technology="
                    + unlockTechnology.ID
            );
        }

        private int FindAvailableGridIndex()
        {
            var occupied = new HashSet<int>(
                LDB.items.dataArray.Where(item => item != copperItem)
                    .Select(item => item.GridIndex)
                    .Concat(
                        LDB.recipes.dataArray.Where(recipe => recipe != copperRecipe)
                            .Select(recipe => recipe.GridIndex)
                    )
            );
            for (int row = 1; row <= 7; row++)
            {
                for (int column = 1; column <= 14; column++)
                {
                    int gridIndex = 2000 + row * 100 + column;
                    if (!occupied.Contains(gridIndex))
                    {
                        return gridIndex;
                    }
                }
            }
            throw new InvalidOperationException(
                "The building crafting page has no available cell."
            );
        }

        private void Update()
        {
            GameHistoryData history = GameMain.history;
            if (
                !PipeApi.IsReady
                || !GameMain.isRunning
                || copperRecipe == null
                || tankRecipeId == 0
                || history == null
                || history == synchronizedHistory
            )
            {
                return;
            }
            synchronizedHistory = history;
            if (history.RecipeUnlocked(tankRecipeId) && !history.RecipeUnlocked(copperRecipe.ID))
            {
                history.UnlockRecipe(copperRecipe.ID);
            }
        }

        private static void ConfigureCopper(Material material)
        {
            material.color = new Color(0.72f, 0.36f, 0.16f);
            material.SetFloat("_Metallic", 0.95f);
            material.SetFloat("_Glossiness", 0.75f);
        }
    }
}
