using System;
using System.Linq;
using FluidPipes.API;
using UnityEngine;

internal static class CopperIconChecks
{
    internal static void Verify(int itemId, Action<bool, string> check)
    {
        ItemProto item = LDB.items.Select(itemId);
        RecipeProto recipe = LDB.recipes.dataArray.Single(candidate =>
            candidate.Results.Contains(itemId)
        );
        Sprite icon = item.iconSprite;
        ItemProto glass = LDB.items.Select(PipeApi.PipeItemId);
        Sprite glassIcon = glass?.iconSprite;
        var atlas = GameMain.iconSet;
        uint glassIndex = glass == null ? 0 : atlas.itemIconIndex[glass.ID];
        check(
            icon != null
                && icon != LDB.items.Select(2001).iconSprite
                && icon != glassIcon
                && recipe.iconSprite == icon,
            "the external copper item and recipe share their own sprite"
        );
        check(
            icon.texture.width == 80
                && icon.texture.height == 80
                && icon.texture.format == TextureFormat.ARGB32
                && icon.texture.GetPixels32().Any(pixel => pixel.a == 0)
                && icon.texture.GetPixels32().Count(pixel => pixel.a > 128) > 800,
            "the copper icon retains readable native atlas dimensions and a visible transparent cutout"
        );
        check(
            atlas.loaded
                && atlas.itemIconIndex[itemId] > 0
                && atlas.itemIconIndex[itemId] != atlas.itemIconIndex[2001]
                && atlas.itemIconIndex[itemId] != glassIndex
                && atlas.recipeIconIndex[recipe.ID] == atlas.itemIconIndex[itemId],
            "native copper item and recipe atlas entries resolve to the independent copper sprite"
        );
        uint itemAtlasIndex = atlas.itemIconIndex[itemId];
        item.Preload(Array.IndexOf(LDB.items.dataArray, item));
        recipe.Preload(Array.IndexOf(LDB.recipes.dataArray, recipe));
        check(
            item.iconSprite == icon
                && recipe.iconSprite == icon
                && atlas.itemIconIndex[itemId] == itemAtlasIndex
                && (
                    glass == null
                    || glass.iconSprite == glassIcon && atlas.itemIconIndex[glass.ID] == glassIndex
                ),
            "prototype reload retains the copper icon and preserves default glass sprite and atlas identity"
        );
    }
}
