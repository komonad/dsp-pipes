using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using xiaoye97;

namespace FluidPipes
{
    internal static class Prototypes
    {
        public static int ItemId = 9460;
        public static int RecipeId = 9460;
        public static int ValveId = 9461;
        public static int ValveRecipeId = 9461;
        public static int ValveModelId = 946;
        public static int UnlockRecipeId;
        public static int UnlockTechId;
        public static bool Ready;
        internal static bool BuiltInItemsEnabled { get; private set; } = true;
        private static bool registrationStarted;
        private static PrefabDesc valveDesc;

        internal static void DisableBuiltInItems()
        {
            if (!PipeTypeRegistry.IsMainThread)
            {
                throw new InvalidOperationException(
                    "Built-in content configuration requires the Unity main thread."
                );
            }
            if (registrationStarted)
            {
                throw new InvalidOperationException(
                    "Built-in content configuration closes when prototype registration begins."
                );
            }

            BuiltInItemsEnabled = false;
            ItemId = 0;
            RecipeId = 0;
            ValveId = 0;
            ValveRecipeId = 0;
            ValveModelId = 0;
            UnlockRecipeId = 0;
            UnlockTechId = 0;
        }

        private static T Copy<T>(T value) =>
            (T)AccessTools.Method(typeof(object), "MemberwiseClone").Invoke(value, null);

        private static IEnumerable<T> Queued<T>()
            where T : Proto
        {
            foreach (string name in new[] { "PreToAdd", "PostToAdd" })
            {
                var groups =
                    AccessTools.Field(typeof(LDBTool), name)?.GetValue(null) as IEnumerable;
                if (groups == null)
                {
                    continue;
                }

                foreach (IEnumerable group in groups)
                {
                    foreach (object proto in group)
                    {
                        if (proto is T value)
                        {
                            yield return value;
                        }
                    }
                }
            }
        }

        internal static void Register()
        {
            registrationStarted = true;
            InitializeTemplate();
            if (!BuiltInItemsEnabled)
            {
                return;
            }

            var usedItems = new HashSet<int>(
                LDB.items.dataArray.Concat(Queued<ItemProto>())
                    .Select(itemPrototype => itemPrototype.ID)
            );
            var usedRecipes = new HashSet<int>(
                LDB.recipes.dataArray.Concat(Queued<RecipeProto>())
                    .Select(recipePrototype => recipePrototype.ID)
            );
            while (usedItems.Contains(ItemId))
            {
                ItemId++;
            }

            while (usedRecipes.Contains(RecipeId))
            {
                RecipeId++;
            }

            if (ItemId >= 12000 || RecipeId >= 12000)
            {
                throw new InvalidOperationException("No free pipe prototype IDs.");
            }

            var item = PipeTypeRegistry.CreateItem(
                ItemId,
                "FluidPipes.Name",
                "FluidPipes.Description"
            );
            var used = new HashSet<int>(
                LDB.items.dataArray.Concat(Queued<ItemProto>())
                    .Select(itemPrototype2 => itemPrototype2.GridIndex)
                    .Concat(
                        LDB.recipes.dataArray.Concat(Queued<RecipeProto>())
                            .Select(recipePrototype2 => recipePrototype2.GridIndex)
                    )
            );
            item.GridIndex = Enumerable
                .Range(1, 7)
                .SelectMany(r => Enumerable.Range(1, 14).Select(c => 2000 + r * 100 + c))
                .First(x => !used.Contains(x));
            LDBTool.PreAddProto(item);
            ItemId = item.ID;
            PipeTypeRegistry.Default(item);
            LDBTool.PreAddProto(BeltRecipe(item, RecipeId));
            RegisterValve();
        }

        private static void InitializeTemplate()
        {
            var item = Copy(LDB.items.Select(2001));
            item.ID = 0;
            item.Name = "FluidPipes.Name";
            item.Description = "FluidPipes.Description";
            item.SID = item.sid = "";
            item.Grade = 1;
            item.Upgrades = new int[0];
            item.IconPath = "";
            PipeIcon.Assign(item);
            item.GridIndex = 0;
            item.BuildIndex = 0;
            item.handcrafts = item.recipes = item.makes = null;
            item.rawMats = null;
            PipeTypeRegistry.InitializeTemplate(item);
        }

        private static void RegisterValve()
        {
            var ids = new HashSet<int>(
                LDB.items.dataArray.Concat(Queued<ItemProto>())
                    .Select(itemPrototype => itemPrototype.ID)
            );
            while (ids.Contains(ValveId))
            {
                ValveId++;
            }

            var recipes = new HashSet<int>(
                LDB.recipes.dataArray.Concat(Queued<RecipeProto>())
                    .Select(recipePrototype => recipePrototype.ID)
            );
            while (recipes.Contains(ValveRecipeId))
            {
                ValveRecipeId++;
            }

            var models = new HashSet<int>(
                LDB.models.dataArray.Concat(Queued<ModelProto>())
                    .Select(modelPrototype => modelPrototype.ID)
            );
            ValveModelId = Enumerable
                .Range(1, Math.Min(ModelProto.MAX_ARRAY_SIZE, LDB.models.dataArray.Length + 64) - 1)
                .Reverse()
                .First(index => !models.Contains(index));
            var source = LDB.items.Select(2040);
            var model = Copy(LDB.models.Select(source.ModelIndex));
            model.ID = ValveModelId;
            model.Name = "FluidPipes.Valve";
            model.SID = model.sid = "";
            model.prefabDesc = Copy(model.prefabDesc);
            model.prefabDesc.modelIndex = ValveModelId;
            model.prefabDesc.isPiler = false;
            model.prefabDesc.isSplitter = true;
            model.prefabDesc.multiLevel = false;
            model.prefabDesc.isPowerConsumer = true;
            model.prefabDesc.workEnergyPerTick = ValvePower.WorkEnergyPerTick;
            model.prefabDesc.idleEnergyPerTick = ValvePower.IdleEnergyPerTick;
            if (model.prefabDesc.portPoses.Length != 2)
            {
                throw new InvalidOperationException("Valve housing must provide two ports.");
            }

            valveDesc = model.prefabDesc;
            ValveMesh.Configure(valveDesc);
            LDBTool.PreAddProto(model);
            ValveModelId = model.ID;
            valveDesc.modelIndex = ValveModelId;
            var item = Copy(source);
            item.ID = ValveId;
            item.Name = "FluidPipes.Valve";
            item.Description = "FluidPipes.ValveDescription";
            item.SID = item.sid = "";
            item.IconPath = "";
            PipeIcon.Assign(item, true);
            item.ModelIndex = ValveModelId;
            item.Grade = 1;
            item.Upgrades = new int[0];
            item.handcrafts = item.recipes = item.makes = null;
            item.rawMats = null;
            item.BuildIndex = 0;
            var used = new HashSet<int>(
                LDB.items.dataArray.Concat(Queued<ItemProto>())
                    .Select(itemPrototype2 => itemPrototype2.GridIndex)
                    .Concat(
                        LDB.recipes.dataArray.Concat(Queued<RecipeProto>())
                            .Select(recipePrototype2 => recipePrototype2.GridIndex)
                    )
            );
            item.GridIndex = Enumerable
                .Range(1, 7)
                .SelectMany(r => Enumerable.Range(1, 14).Select(c => 2000 + r * 100 + c))
                .First(index2 => !used.Contains(index2));
            LDBTool.PreAddProto(item);
            LDBTool.PreAddProto(BeltRecipe(item, ValveRecipeId));
        }

        private static RecipeProto BeltRecipe(ItemProto item, int itemId)
        {
            var source = LDB.recipes.dataArray.First(recipePrototype =>
                recipePrototype.Results.Contains(2001)
            );
            return new RecipeProto
            {
                ID = itemId,
                Name = item.Name,
                Description = item.Description,
                IconPath = "",
                Type = source.Type,
                GridIndex = item.GridIndex,
                Handcraft = source.Handcraft,
                NonProductive = source.NonProductive,
                TimeSpend = source.TimeSpend,
                Items = (int[])source.Items.Clone(),
                ItemCounts = (int[])source.ItemCounts.Clone(),
                Results = new[] { item.ID },
                ResultCounts = new[] { source.ResultCounts[Array.IndexOf(source.Results, 2001)] },
            };
        }

        private static void PlaceBuildBar(params ItemProto[] items)
        {
            if (Integration.BuildBarIntegration.TryPlaceUpperTransportRow(items))
            {
                return;
            }
            var own = new HashSet<int>(items.Select(itemPrototype => itemPrototype.ID));
            var occupied = new HashSet<int>(
                LDB.items.dataArray.Where(itemPrototype2 =>
                        !own.Contains(itemPrototype2.ID) && itemPrototype2.BuildIndex > 0
                    )
                    .Select(itemPrototype3 => itemPrototype3.BuildIndex)
            );
            var bar =
                AccessTools.Field(typeof(UIBuildMenu), "protos")?.GetValue(null) as ItemProto[,];
            if (bar != null)
            {
                for (int category = 1; category <= 9; category++)
                {
                    for (int slot = 1; slot <= 12; slot++)
                    {
                        if (bar[category, slot] != null && !own.Contains(bar[category, slot].ID))
                        {
                            occupied.Add(category * 100 + slot);
                        }
                    }
                }
            }

            var queued =
                AccessTools.Field(typeof(LDBTool), "BuildBarDict")?.GetValue(null)
                as Dictionary<int, Dictionary<int, int>>;
            if (queued != null)
            {
                foreach (var category in queued)
                {
                    foreach (var slot in category.Value)
                    {
                        if (!own.Contains(slot.Value))
                        {
                            occupied.Add(category.Key * 100 + slot.Key);
                        }
                    }
                }
            }

            var categories = new[]
            {
                LDB.items.Select(2001).BuildIndex / 100,
                LDB.items.Select(2106).BuildIndex / 100,
            }
                .Concat(Enumerable.Range(1, 8))
                .Where(c => c >= 1 && c <= 8)
                .Distinct()
                .ToArray();
            // Native prototype tables reserve twelve indices; construction popups instantiate ten buttons.
            var candidates = categories
                .Select(c =>
                    Enumerable
                        .Range(1, 10)
                        .Select(slot => c * 100 + slot)
                        .Where(index => !occupied.Contains(index))
                        .ToArray()
                )
                .ToArray();
            var positions =
                candidates.FirstOrDefault(slots => slots.Length >= items.Length)
                ?? candidates.SelectMany(slots => slots).Take(items.Length).ToArray();
            if (positions.Length < items.Length)
            {
                throw new InvalidOperationException(
                    "No free construction bar slots for fluid transport."
                );
            }

            for (int index2 = 0; index2 < items.Length; index2++)
            {
                items[index2].BuildIndex = positions[index2];
                LDBTool.SetBuildBar(
                    positions[index2] / 100,
                    positions[index2] % 100,
                    items[index2].ID
                );
            }
        }

        internal static void Finish()
        {
            if (!BuiltInItemsEnabled)
            {
                return;
            }

            int length = Math.Max(
                LDB.models.modelArray?.Length ?? 0,
                LDB.models.dataArray.Max(modelPrototype => modelPrototype.ID) + 1
            );
            if (length > ModelProto.MAX_ARRAY_SIZE)
            {
                throw new InvalidOperationException("Model index exceeds the engine limit.");
            }

            var modelArray = new ModelProto[length];
            foreach (var model in LDB.models.dataArray)
            {
                modelArray[model.ID] = model;
            }

            LDB.models.modelArray = modelArray;
            LDB.models.Select(ValveModelId).prefabDesc = valveDesc;
            LDB.items.OnAfterDeserialize();
            LDB.recipes.OnAfterDeserialize();
            var item = LDB.items.Select(ItemId);
            var sourceRecipe = LDB.recipes.dataArray.First(recipePrototype =>
                recipePrototype.Results.Contains(2106)
            );
            UnlockRecipeId = sourceRecipe.ID;
            var tech = LDB.techs.dataArray.First(x =>
                x.UnlockRecipes != null && x.UnlockRecipes.Contains(sourceRecipe.ID)
            );
            UnlockTechId = tech.ID;
            item.PreTechOverride = tech.ID;
            tech.UnlockRecipes = tech.UnlockRecipes.Concat(new[] { RecipeId }).Distinct().ToArray();
            tech.UnlockRecipes = tech
                .UnlockRecipes.Concat(new[] { ValveRecipeId })
                .Distinct()
                .ToArray();
            LDB.items.Select(ValveId).PreTechOverride = tech.ID;
            for (int index = 0; index < LDB.items.dataArray.Length; index++)
            {
                var proto = LDB.items.dataArray[index];
                proto.recipes = null;
                proto.rawMats = null;
                proto.Preload(index);
            }
            for (int index2 = 0; index2 < LDB.recipes.dataArray.Length; index2++)
            {
                LDB.recipes.dataArray[index2].Preload(index2);
            }

            foreach (var proto in LDB.techs.dataArray)
            {
                proto.Preload();
                proto.Preload2();
            }
            PlaceBuildBar(item, LDB.items.Select(ValveId));
            TransportItemRegistry.Refresh();
        }
    }
}
