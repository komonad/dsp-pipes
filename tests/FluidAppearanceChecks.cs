using System;
using FluidPipes;
using UnityEngine;

public sealed partial class RuntimeChecks
{
    private void FluidColors()
    {
        int[] items = { 1000, 1007, 1114, 1116, 1120, 1121 };
        Color[] tankColors =
        {
            new Color(0.4970185f, 0.6449671f, 0.6886792f),
            new Color(0.2924528f, 0.1669188f, 0.2924528f),
            new Color(0.5754717f, 0.4227817f, 0.3067373f),
            new Color(0.7169812f, 0.6327461f, 0.3145247f),
            new Color(0.7066126f, 0.7452830f, 0.7225357f),
            new Color(0.6200380f, 0.7830189f, 0.5798772f),
        };
        for (int index = 0; index < items.Length; index++)
        {
            Color expected = tankColors[index].linear * 1.5f;
            Color actual = ReadFluidTint(PipeVisuals.LiquidMaterial(items[index]));
            Check(
                ColorDistance(actual, expected) < 0.003f && Math.Abs(actual.a - 0.68f) < 0.001f,
                "pipe tint matches native tank color and linear exposure for item " + items[index]
            );
        }

        var icons = GameMain.iconSet;
        int offset = checked((int)icons.itemIconIndex[1000] * 40 + 20);
        float[] original = new float[4];
        Array.Copy(icons.itemDescArr, offset, original, 0, 4);
        var material = PipeVisuals.LiquidMaterial(1000);
        Texture initialTexture = material.mainTexture;
        int geometryBuilds = PipeVisuals.NodeRebuilds;
        try
        {
            icons.itemDescArr[offset] = 1;
            icons.itemDescArr[offset + 1] = 0.3f;
            icons.itemDescArr[offset + 2] = 0.1f;
            var updated = PipeVisuals.LiquidMaterial(1000);
            Color uploaded = ReadFluidTint(updated);
            Check(
                ReferenceEquals(material, updated)
                    && updated.mainTexture != initialTexture
                    && uploaded.r > 1.49f
                    && PipeVisuals.NodeRebuilds == geometryBuilds,
                "native palette changes refresh cached HDR tints while preserving material and geometry ownership"
            );
        }
        finally
        {
            Array.Copy(original, 0, icons.itemDescArr, offset, 4);
            PipeVisuals.LiquidMaterial(1000);
        }
    }

    private static float ColorDistance(Color first, Color second) =>
        new Vector3(first.r - second.r, first.g - second.g, first.b - second.b).magnitude;

    private static Color ReadFluidTint(Material material)
    {
        var target = RenderTexture.GetTemporary(
            1,
            1,
            0,
            RenderTextureFormat.ARGBFloat,
            RenderTextureReadWrite.Linear
        );
        var previous = RenderTexture.active;
        bool previousSrgbWrite = GL.sRGBWrite;
        var pixels = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
        try
        {
            GL.sRGBWrite = false;
            Graphics.Blit(material.mainTexture, target);
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
            return pixels.GetPixel(0, 0);
        }
        finally
        {
            RenderTexture.active = previous;
            GL.sRGBWrite = previousSrgbWrite;
            RenderTexture.ReleaseTemporary(target);
            UnityEngine.Object.Destroy(pixels);
        }
    }
}
