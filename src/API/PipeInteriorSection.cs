using System;
using UnityEngine;

namespace FluidPipes.API
{
    /// <summary>纵向内部构件的周界形状。</summary>
    public enum PipeInteriorSectionKind
    {
        /// <summary>圆形截面。</summary>
        Circle,

        /// <summary>带圆角的轴对齐矩形。</summary>
        RoundedRectangle,
    }

    /// <summary>按外管壁半径归一化的不可变构件尺寸。</summary>
    /// <remarks>表面 U 沿周界的归一化距离，包括圆角弧段。接缝在 U = 0 和 U = 1 处复制同一位置。纵向 V 遵循外观的纹理坐标映射。</remarks>
    public sealed class PipeInteriorSection
    {
        /// <summary>周界形状。</summary>
        public PipeInteriorSectionKind Kind { get; }

        /// <summary>横向全宽与外管壁半径之比。</summary>
        public float WidthFraction { get; }

        /// <summary>径向全高与外管壁半径之比。</summary>
        public float HeightFraction { get; }

        /// <summary>圆角半径与外管壁半径之比。</summary>
        public float BevelFraction { get; }

        /// <summary>包围截面的半径与外管壁半径之比。</summary>
        public float RadiusFraction { get; }

        private PipeInteriorSection(
            PipeInteriorSectionKind kind,
            float width,
            float height,
            float bevel,
            float radius
        )
        {
            Kind = kind;
            WidthFraction = width;
            HeightFraction = height;
            BevelFraction = bevel;
            RadiusFraction = radius;
        }

        /// <summary>定义圆形构件截面。</summary>
        /// <param name="radiusFraction">半径与外管壁半径之比，范围为 0.01 到 0.85。</param>
        /// <exception cref="ArgumentOutOfRangeException">半径超出有限的支持区间。</exception>
        public static PipeInteriorSection Circle(float radiusFraction)
        {
            RequireRange(radiusFraction, 0.01f, 0.85f, nameof(radiusFraction));
            return new PipeInteriorSection(
                PipeInteriorSectionKind.Circle,
                radiusFraction * 2,
                radiusFraction * 2,
                radiusFraction,
                radiusFraction
            );
        }

        /// <summary>定义带圆形圆角的矩形构件。</summary>
        /// <param name="widthFraction">横向全宽与外管壁半径之比，范围为 0.02 到 1.7。</param>
        /// <param name="heightFraction">径向全高与外管壁半径之比，范围为 0.02 到 1.7。</param>
        /// <param name="bevelFraction">圆角半径与外管壁半径之比，范围为 0 到较小边的一半。</param>
        /// <exception cref="ArgumentOutOfRangeException">某个尺寸超出其有限的支持区间。</exception>
        public static PipeInteriorSection RoundedRectangle(
            float widthFraction,
            float heightFraction,
            float bevelFraction
        )
        {
            RequireRange(widthFraction, 0.02f, 1.7f, nameof(widthFraction));
            RequireRange(heightFraction, 0.02f, 1.7f, nameof(heightFraction));
            RequireRange(
                bevelFraction,
                0,
                Mathf.Min(widthFraction, heightFraction) * 0.5f,
                nameof(bevelFraction)
            );
            float radius = new Vector2(widthFraction, heightFraction).magnitude * 0.5f;
            return new PipeInteriorSection(
                PipeInteriorSectionKind.RoundedRectangle,
                widthFraction,
                heightFraction,
                bevelFraction,
                radius
            );
        }

        internal bool Matches(PipeInteriorSection other) =>
            other != null
            && Kind == other.Kind
            && Mathf.Abs(WidthFraction - other.WidthFraction) <= 0.00001f
            && Mathf.Abs(HeightFraction - other.HeightFraction) <= 0.00001f
            && Mathf.Abs(BevelFraction - other.BevelFraction) <= 0.00001f;

        private static void RequireRange(
            float value,
            float minimum,
            float maximum,
            string parameter
        )
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < minimum || value > maximum)
            {
                throw new ArgumentOutOfRangeException(parameter);
            }
        }
    }
}
