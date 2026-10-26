using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace FluidPipes
{
    internal static class PipeItemIcons
    {
        private const int IconSize = 80;
        private static readonly FieldInfo iconSprite = AccessTools.Field(
            typeof(ItemProto),
            "_iconSprite"
        );

        internal static void Set(ItemProto prototype, Sprite icon)
        {
            PipeTypeRegistry.RequireRegistration();
            if (prototype == null)
            {
                throw new ArgumentNullException(nameof(prototype));
            }
            if (icon == null)
            {
                throw new ArgumentNullException(nameof(icon));
            }
            if (prototype.ID <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(prototype));
            }

            Texture2D texture = icon.texture;
            if (
                texture == null
                || !texture.isReadable
                || texture.format != TextureFormat.ARGB32
                || texture.width != IconSize
                || texture.height != IconSize
                || icon.packed
                || icon.rect != new Rect(0, 0, IconSize, IconSize)
            )
            {
                throw new ArgumentException(
                    "Item icons use a full 80-by-80 sprite on a readable, unpacked ARGB32 texture.",
                    nameof(icon)
                );
            }

            // The native atlas copies this texture region after prototype registration completes.
            prototype.IconPath = "";
            iconSprite.SetValue(prototype, icon);
        }
    }
}
