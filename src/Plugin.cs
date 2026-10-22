using System;
using System.IO;
using BepInEx;
using CommonAPI;
using CommonAPI.Systems;
using CommonAPI.Systems.ModLocalization;
using crecheng.DSPModSave;
using HarmonyLib;
using UnityEngine;
using xiaoye97;

namespace FluidPipes
{
    [BepInPlugin(Guid, "Fluid Pipes", API.PipeApi.ModVersion)]
    [BepInDependency(CommonAPIPlugin.GUID)]
    [BepInDependency("me.xiaoye97.plugin.Dyson.LDBTool")]
    [BepInDependency(DSPModSavePlugin.MODGUID)]
    [CommonAPISubmoduleDependency(nameof(ProtoRegistry), nameof(LocalizationModule))]
    public sealed class Plugin : BaseUnityPlugin, IModCanSave
    {
        public const string Guid = API.PipeApi.PluginGuid;
        private Harmony harmony;
        private GameHistoryData unlockedHistory;

        private void Awake()
        {
            PipeConfiguration.Load(Config, Logger);
            LocalizationModule.RegisterTranslation("FluidPipes.Name", "Fluid Pipe", "流体管道", "");
            LocalizationModule.RegisterTranslation(
                "FluidPipes.Description",
                "Connected pipes share a single-item inventory. Capacity: 100 per segment.",
                "连通管道共享单一物品库存。每段容量100。",
                ""
            );
            LocalizationModule.RegisterTranslation(
                "FluidPipes.Valve",
                "Fluid Valve",
                "流体阀门",
                ""
            );
            LocalizationModule.RegisterTranslation(
                "FluidPipes.ValveDescription",
                "Two isolated fluid spaces with controlled transfer. Pump power: 100 kW.",
                "两端口阀门。两侧储液空间独立，支持限速、单向输送和目标液位控制。泵送需要供电，工作功率100 kW。",
                ""
            );
            LDBTool.PreAddDataAction += Prototypes.Register;
            LDBTool.PostAddDataAction += Prototypes.Finish;
            harmony = new Harmony(Guid);
            harmony.PatchAll();
        }

        private void OnDestroy()
        {
            LDBTool.PreAddDataAction -= Prototypes.Register;
            LDBTool.PostAddDataAction -= Prototypes.Finish;
            harmony?.UnpatchSelf();
            FluidInserterVisuals.Dispose();
            PipeVisuals.Dispose();
            PipeIcon.Dispose();
            PipePanel.Dispose();
        }

        private void LateUpdate()
        {
            if (!Prototypes.Ready)
            {
                return;
            }

            var factory = GameMain.localPlanet?.factory;
            var camera = GameCamera.main;
            if (factory == null || camera == null)
            {
                return;
            }

            var model = factory.planet.factoryModel;
            if (
                model == null
                || model.disableFactoryEntities
                || model.gpuiManager == null
                || !model.gpuiManager.renderEntity
            )
            {
                return;
            }

            PipeVisuals.Draw(factory, camera);
            FluidInserterVisuals.Draw(factory, camera);
        }

        private void Update()
        {
            if (Prototypes.Ready)
            {
                PipePanel.Update();
            }

            var history = GameMain.history;
            if (
                Prototypes.Ready
                && GameMain.isRunning
                && history != null
                && history != unlockedHistory
            )
            {
                unlockedHistory = history;
                if (history.RecipeUnlocked(Prototypes.UnlockRecipeId))
                {
                    if (!history.RecipeUnlocked(Prototypes.RecipeId))
                    {
                        history.UnlockRecipe(Prototypes.RecipeId);
                    }

                    if (!history.RecipeUnlocked(Prototypes.ValveRecipeId))
                    {
                        history.UnlockRecipe(Prototypes.ValveRecipeId);
                    }
                }
            }
            if (
                PipeConfiguration.EnableDebugHotkey
                && Prototypes.Ready
                && GameMain.mainPlayer != null
                && Input.GetKeyDown(KeyCode.F8)
            )
            {
                GameMain.mainPlayer.TryAddItemToPackage(Prototypes.ItemId, 200, 0, true, 0);
                GameMain.mainPlayer.TryAddItemToPackage(Prototypes.ValveId, 20, 0, true, 0);
            }
        }

        private void OnGUI()
        {
            PipePanel.DrawLabels();
        }

        public void Export(BinaryWriter writer)
        {
            PipeRuntime.Export(writer);
        }

        public void Import(BinaryReader reader)
        {
            PipeRuntime.Import(reader);
        }

        public void IntoOtherSave()
        {
            PipeRuntime.Clear();
            PipeVisuals.Clear();
            FluidInserterVisuals.Clear();
        }
    }
}
