using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace FluidPipes
{
    internal static class PipeIcon
    {
        private const int Size = 80;

        private sealed class Asset
        {
            public Sprite Sprite;
            public Texture2D Texture;
        }

        private static readonly Dictionary<string, Asset> assets = new Dictionary<string, Asset>();

        public static void Assign(ItemProto item, bool valve = false)
        {
            string name = valve ? "Valve" : "Pipe";
            if (!assets.TryGetValue(name, out var asset))
            {
                asset = Create(name);
                assets.Add(name, asset);
            }
            AccessTools.Field(typeof(ItemProto), "_iconSprite").SetValue(item, asset.Sprite);
        }

        private static Asset Create(string name)
        {
            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                using (
                    var resource = typeof(PipeIcon).Assembly.GetManifestResourceStream(
                        "FluidPipes.Assets." + name + "Icon.png"
                    )
                )
                using (var buffer = new MemoryStream())
                {
                    if (resource == null)
                    {
                        throw new InvalidOperationException("Pipe icon resource is unavailable.");
                    }

                    resource.CopyTo(buffer);
                    if (!source.LoadImage(buffer.ToArray()))
                    {
                        throw new InvalidOperationException("Pipe icon resource is invalid.");
                    }
                }
                var pixels = source.GetPixels32();
                var scaled = new Color32[Size * Size];
                for (int y = 0; y < Size; y++)
                {
                    for (int x = 0; x < Size; x++)
                    {
                        int left = x * source.width / Size;
                        int right = (x + 1) * source.width / Size;
                        int bottom = y * source.height / Size;
                        int top = (y + 1) * source.height / Size;
                        long alpha = 0;
                        long red = 0;
                        long green = 0;
                        long blue = 0;
                        for (int row = bottom; row < top; row++)
                        {
                            for (int col = left; col < right; col++)
                            {
                                var p = pixels[row * source.width + col];
                                alpha += p.a;
                                red += p.r * p.a;
                                green += p.g * p.a;
                                blue += p.b * p.a;
                            }
                        }
                        // 按 alpha 加权缩放可在透明像素背景下保留玻璃边缘。
                        if (alpha > 0)
                        {
                            scaled[y * Size + x] = new Color32(
                                (byte)(red / alpha),
                                (byte)(green / alpha),
                                (byte)(blue / alpha),
                                (byte)(alpha / ((right - left) * (top - bottom)))
                            );
                        }
                    }
                }
                // 原生图标图集为每个 sprite 复制 80 像素的 ARGB32 纹理区域。
                var texture = new Texture2D(Size, Size, TextureFormat.ARGB32, false)
                {
                    name = "FluidPipes." + name + "ItemIcon",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };
                texture.SetPixels32(scaled);
                texture.Apply(false, false);
                var sprite = Sprite.Create(
                    texture,
                    new Rect(0, 0, Size, Size),
                    new Vector2(0.5f, 0.5f),
                    Size,
                    0,
                    SpriteMeshType.FullRect
                );
                sprite.name = "FluidPipes." + name + "ItemIcon";
                return new Asset { Texture = texture, Sprite = sprite };
            }
            finally
            {
                UnityEngine.Object.Destroy(source);
            }
        }

        public static void Dispose()
        {
            foreach (var asset in assets.Values)
            {
                if (asset.Sprite != null)
                {
                    UnityEngine.Object.Destroy(asset.Sprite);
                }

                if (asset.Texture != null)
                {
                    UnityEngine.Object.Destroy(asset.Texture);
                }
            }
            assets.Clear();
        }
    }
}
