using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using BepInEx;
using FluidPipes.API;
using xiaoye97;

[BepInPlugin("local.fluidpipes.content-optout", "Fluid Pipes Content Opt-Out Checks", "1.0.0")]
[BepInDependency(PipeApi.PluginGuid)]
[BepInDependency("example.copperpipes")]
[BepInDependency("me.xiaoye97.plugin.Dyson.LDBTool")]
public sealed class EarlyContentOptOut : BaseUnityPlugin
{
    private readonly List<string> results = new List<string>();
    private int[] baselineItems;
    private int[] baselineRecipes;
    private int[] baselineModels;
    private Dictionary<int, int[]> baselineUnlocks;
    private bool completed;

    private void Awake()
    {
        Check(!PipeApi.IsReady, "external startup executes before prototype initialization");
        Exception backgroundError = null;
        var backgroundCall = new Thread(() =>
        {
            try
            {
                PipeApi.DisableBuiltInItems();
            }
            catch (Exception exception)
            {
                backgroundError = exception;
            }
        });
        backgroundCall.Start();
        backgroundCall.Join();
        Check(
            backgroundError is InvalidOperationException,
            "public content opt-out rejects calls from a worker thread"
        );
        PipeApi.DisableBuiltInItems();
        PipeApi.DisableBuiltInItems();
        Check(
            PipeApi.PipeItemId == 0 && PipeApi.ValveItemId == 0,
            "repeated public content opt-out calls complete before registration"
        );
        LDBTool.PreAddDataAction += SnapshotDatabase;
    }

    private void SnapshotDatabase()
    {
        baselineItems = LDB.items.dataArray.Select(item => item.ID).ToArray();
        baselineRecipes = LDB.recipes.dataArray.Select(recipe => recipe.ID).ToArray();
        baselineModels = LDB.models.dataArray.Select(model => model.ID).ToArray();
        baselineUnlocks = LDB.techs.dataArray.ToDictionary(
            technology => technology.ID,
            technology => (technology.UnlockRecipes ?? Array.Empty<int>()).ToArray()
        );
    }

    private void Update()
    {
        if (completed || !PipeApi.IsReady || GameMain.iconSet == null || !GameMain.iconSet.loaded)
        {
            return;
        }
        completed = true;
        try
        {
            Check(
                PipeApi.PipeItemId == 0
                    && PipeApi.ValveItemId == 0
                    && PipeApi.PipeRecipeId == 0
                    && PipeApi.ValveRecipeId == 0
                    && !PipeTypes.TryGet(PipeApi.PluginGuid + ":default", out _),
                "the initialized library exposes zero built-in IDs and only external pipe types"
            );
            Check(
                PipeTypes.TryGet("example.copperpipes:copper", out var copper),
                "the independent consumer registers from the library-only template"
            );
            RecipeProto copperRecipe = LDB.recipes.dataArray.Single(recipe =>
                recipe.Results.Contains(copper.ItemId)
            );
            Check(
                LDB.items.dataArray.Select(item => item.ID)
                    .Except(baselineItems)
                    .SequenceEqual(new[] { copper.ItemId })
                    && LDB.recipes.dataArray.Select(recipe => recipe.ID)
                        .Except(baselineRecipes)
                        .SequenceEqual(new[] { copperRecipe.ID })
                    && new HashSet<int>(LDB.models.dataArray.Select(model => model.ID)).SetEquals(
                        baselineModels
                    ),
                "database additions consist of the copper item and recipe while model prototypes remain unchanged"
            );
            Check(
                LDB.items.dataArray.All(item =>
                    item.Name != "FluidPipes.Name" && item.Name != "FluidPipes.Valve"
                )
                    && LDB.recipes.dataArray.All(recipe =>
                        recipe.Name != "FluidPipes.Name" && recipe.Name != "FluidPipes.Valve"
                    ),
                "the database contains no built-in fluid pipe or valve prototypes"
            );
            Check(
                LDB.techs.dataArray.All(technology =>
                    (technology.UnlockRecipes ?? Array.Empty<int>())
                        .Where(recipeId => recipeId != copperRecipe.ID)
                        .SequenceEqual(baselineUnlocks[technology.ID])
                ),
                "technology unlock changes are limited to the external copper recipe"
            );
            bool rejectedLateCall = false;
            try
            {
                PipeApi.DisableBuiltInItems();
            }
            catch (InvalidOperationException)
            {
                rejectedLateCall = true;
            }
            Check(rejectedLateCall, "content opt-out closes after prototype registration begins");
            results.Add("Failures=0");
        }
        catch (Exception exception)
        {
            results.Add("FAIL " + exception);
            results.Add("Failures=1");
            Logger.LogError(exception);
        }
        File.WriteAllLines(
            Path.Combine(Path.GetDirectoryName(Info.Location), "disabled-content-bootstrap.txt"),
            results
        );
    }

    private void Check(bool condition, string description)
    {
        if (!condition)
        {
            throw new InvalidOperationException(description);
        }
        results.Add("PASS " + description);
    }

    private void OnDestroy()
    {
        LDBTool.PreAddDataAction -= SnapshotDatabase;
    }
}
