using UnityEngine;

namespace FluidPipes
{
    internal static class FluidAppearance
    {
        private const int DescriptorStride = 40;
        private const int TankColorOffset = 20;

        internal static Color LinearColor(int itemId)
        {
            var icons = GameMain.iconSet;
            if (icons?.itemIconIndex == null || itemId <= 0 || itemId >= icons.itemIconIndex.Length)
            {
                return Color.gray.linear;
            }
            uint icon = icons.itemIconIndex[itemId];
            long descriptor = (long)icon * DescriptorStride;
            if (
                icon == 0
                || icons.itemDescArr == null
                || descriptor + TankColorOffset + 3 >= icons.itemDescArr.Length
            )
            {
                return Color.gray.linear;
            }
            int offset = (int)descriptor + TankColorOffset;
            var color = new Color(
                icons.itemDescArr[offset],
                icons.itemDescArr[offset + 1],
                icons.itemDescArr[offset + 2],
                icons.itemDescArr[offset + 3]
            );
            if (color == Color.clear)
            {
                offset = (int)descriptor;
                color = new Color(
                    icons.itemDescArr[offset],
                    icons.itemDescArr[offset + 1],
                    icons.itemDescArr[offset + 2]
                );
            }
            // 原生储液罐着色器读取 IconDesc.reserved0，并应用此 sRGB 转换与曝光。
            return new Color(
                LinearChannel(color.r),
                LinearChannel(color.g),
                LinearChannel(color.b),
                1
            );
        }

        private static float LinearChannel(float value) =>
            Mathf.Pow(Mathf.Max(0, (value + 0.055f) / 1.055f), 2.4f) * 1.5f;
    }
}
