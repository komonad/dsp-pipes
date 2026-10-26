using System;
using System.Collections.Generic;
using System.Threading;
using FluidPipes.API;
using UnityEngine;

public sealed partial class RuntimeChecks
{
    private void PrepareIconApiChecks()
    {
        var resources = new List<UnityEngine.Object>();
        try
        {
            var draft = PipeTypes.CreateItem(
                11998,
                "FluidPipes.Checks.icon",
                "FluidPipes.Description"
            );
            Sprite originalIcon = draft.iconSprite;
            Sprite icon = CreateIconApiSprite(resources, 80, TextureFormat.ARGB32);
            ExpectTypeException<ArgumentNullException>(
                () => PipeTypes.SetItemIcon(null, icon),
                "icon registration rejects a null item draft"
            );
            ExpectTypeException<ArgumentNullException>(
                () => PipeTypes.SetItemIcon(draft, null),
                "icon registration rejects a null sprite"
            );
            ExpectTypeException<ArgumentOutOfRangeException>(
                () => PipeTypes.SetItemIcon(new ItemProto { ID = 0 }, icon),
                "icon registration requires a positive item ID"
            );
            foreach (
                Sprite invalidIcon in new[]
                {
                    CreateIconApiSprite(resources, 64, TextureFormat.ARGB32),
                    CreateIconApiSprite(resources, 80, TextureFormat.RGBA32),
                    CreateIconApiSprite(resources, 80, TextureFormat.ARGB32, readable: false),
                    CreateIconApiSprite(resources, 80, TextureFormat.ARGB32, cropped: true),
                }
            )
            {
                ExpectTypeException<ArgumentException>(
                    () => PipeTypes.SetItemIcon(draft, invalidIcon),
                    "icon registration rejects a sprite outside the native atlas contract"
                );
            }
            Sprite destroyedIcon = CreateIconApiSprite(resources, 80, TextureFormat.ARGB32);
            UnityEngine.Object.DestroyImmediate(destroyedIcon);
            ExpectTypeException<ArgumentNullException>(
                () => PipeTypes.SetItemIcon(draft, destroyedIcon),
                "icon registration rejects a destroyed Unity sprite"
            );
            Check(
                draft.iconSprite == originalIcon,
                "rejected icon assignments preserve the draft sprite"
            );

            Exception backgroundError = null;
            var backgroundCall = new Thread(() =>
            {
                try
                {
                    PipeTypes.SetItemIcon(draft, icon);
                }
                catch (Exception exception)
                {
                    backgroundError = exception;
                }
            });
            backgroundCall.Start();
            backgroundCall.Join();
            Check(
                backgroundError is InvalidOperationException && draft.iconSprite == originalIcon,
                "icon assignment requires the registration thread and preserves state on rejection"
            );
            draft.IconPath = "fixture-icon-path";
            PipeTypes.SetItemIcon(draft, icon);
            Check(
                draft.iconSprite == icon && string.IsNullOrEmpty(draft.IconPath),
                "valid icon assignment borrows the supplied sprite and clears resource lookup"
            );
            PipeTypes.SetItemIcon(draft, originalIcon);
        }
        finally
        {
            for (int index = resources.Count - 1; index >= 0; index--)
            {
                if (resources[index] != null)
                {
                    UnityEngine.Object.DestroyImmediate(resources[index]);
                }
            }
        }
    }

    private static Sprite CreateIconApiSprite(
        ICollection<UnityEngine.Object> resources,
        int size,
        TextureFormat format,
        bool readable = true,
        bool cropped = false
    )
    {
        var texture = new Texture2D(size, size, format, false);
        resources.Add(texture);
        texture.Apply(false, !readable);
        var sprite = Sprite.Create(
            texture,
            new Rect(0, 0, cropped ? size / 2 : size, size),
            new Vector2(0.5f, 0.5f),
            100,
            0,
            SpriteMeshType.FullRect
        );
        resources.Add(sprite);
        return sprite;
    }
}
