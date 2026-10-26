using System;
using System.IO;
using UnityEngine;

namespace CopperPipes
{
    internal static class CopperPipeIcon
    {
        private const int IconSize = 80;
        private const string ResourceName = "CopperPipes.Assets.CopperPipe.png";
        private static Texture2D texture;
        private static Sprite sprite;

        internal static Sprite Load()
        {
            if (sprite != null)
            {
                return sprite;
            }

            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                using (
                    Stream resource = typeof(CopperPipeIcon).Assembly.GetManifestResourceStream(
                        ResourceName
                    )
                )
                using (var buffer = new MemoryStream())
                {
                    if (resource == null)
                    {
                        throw new InvalidOperationException(
                            "The copper pipe icon resource is missing."
                        );
                    }
                    resource.CopyTo(buffer);
                    if (!source.LoadImage(buffer.ToArray()))
                    {
                        throw new InvalidOperationException(
                            "The copper pipe icon resource is invalid."
                        );
                    }
                }
                if (source.width < IconSize || source.height < IconSize)
                {
                    throw new InvalidOperationException(
                        "The copper pipe icon requires at least 80 pixels per side."
                    );
                }

                // The native icon atlas copies a readable 80-pixel ARGB32 texture region.
                texture = new Texture2D(IconSize, IconSize, TextureFormat.ARGB32, false)
                {
                    name = "CopperPipes.ItemIcon",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.DontUnloadUnusedAsset,
                };
                texture.SetPixels32(Resize(source));
                texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
                sprite = Sprite.Create(
                    texture,
                    new Rect(0, 0, IconSize, IconSize),
                    new Vector2(0.5f, 0.5f),
                    IconSize,
                    0,
                    SpriteMeshType.FullRect
                );
                sprite.name = "CopperPipes.ItemIcon";
                sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
                return sprite;
            }
            catch
            {
                Release();
                throw;
            }
            finally
            {
                UnityEngine.Object.Destroy(source);
            }
        }

        private static Color32[] Resize(Texture2D source)
        {
            Color32[] sourcePixels = source.GetPixels32();
            var scaledPixels = new Color32[IconSize * IconSize];
            for (int targetRow = 0; targetRow < IconSize; targetRow++)
            {
                int bottom = targetRow * source.height / IconSize;
                int top = (targetRow + 1) * source.height / IconSize;
                for (int targetColumn = 0; targetColumn < IconSize; targetColumn++)
                {
                    int left = targetColumn * source.width / IconSize;
                    int right = (targetColumn + 1) * source.width / IconSize;
                    long alpha = 0;
                    long red = 0;
                    long green = 0;
                    long blue = 0;
                    for (int sourceRow = bottom; sourceRow < top; sourceRow++)
                    {
                        for (int sourceColumn = left; sourceColumn < right; sourceColumn++)
                        {
                            Color32 pixel = sourcePixels[sourceRow * source.width + sourceColumn];
                            alpha += pixel.a;
                            red += pixel.r * pixel.a;
                            green += pixel.g * pixel.a;
                            blue += pixel.b * pixel.a;
                        }
                    }
                    if (alpha > 0)
                    {
                        // Premultiplied averaging preserves the silhouette at transparent edges.
                        scaledPixels[targetRow * IconSize + targetColumn] = new Color32(
                            (byte)(red / alpha),
                            (byte)(green / alpha),
                            (byte)(blue / alpha),
                            (byte)(alpha / ((right - left) * (top - bottom)))
                        );
                    }
                }
            }
            return scaledPixels;
        }

        internal static void Release()
        {
            if (sprite != null)
            {
                UnityEngine.Object.Destroy(sprite);
                sprite = null;
            }
            if (texture != null)
            {
                UnityEngine.Object.Destroy(texture);
                texture = null;
            }
        }
    }
}
