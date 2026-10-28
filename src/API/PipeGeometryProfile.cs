using System;

namespace FluidPipes.API
{
    /// <summary>程序化管道类型的不可变尺寸，以 Unity 世界单位表示。</summary>
    public sealed class PipeGeometryProfile
    {
        /// <summary>标准管道尺寸，供省略几何配置的类型共享。</summary>
        public static PipeGeometryProfile Default { get; } = new PipeGeometryProfile();

        /// <summary>几何包络半径，单位为世界单位。可见管壁半径由外观的 WallRadiusFraction 比例确定。</summary>
        public float Radius { get; }

        /// <summary>从原生实体位置沿径向到中心线的偏移，单位为世界单位。</summary>
        public float CenterHeight { get; }

        /// <summary>施加于管箍厚度和轴向深度的倍率；接头内孔随管壁半径适配。</summary>
        public float FittingScale { get; }

        /// <summary>直段和端点中部支撑环的轴向宽度，单位为世界单位。零表示省略支撑环。</summary>
        public float SupportRingWidth { get; }

        /// <summary>孤立节点沿自身纵轴的半长度，单位为世界单位。</summary>
        public float IsolatedHalfLength { get; }

        /// <summary>分别配置截面尺寸与孤立节点的纵向长度。</summary>
        /// <param name="radius">几何包络半径，范围为 0.08 到 2 世界单位。</param>
        /// <param name="centerHeight">相对实体锚点的径向高度，范围为 0 到 2 世界单位。</param>
        /// <param name="fittingScale">接头尺寸倍率，范围为 0.75 到 1.5。</param>
        /// <param name="supportRingWidth">支撑环轴向宽度，范围为 0 到外管壁半径。</param>
        /// <param name="isolatedHalfLength">孤立节点的半长度，范围为 0.05 到 4 世界单位；null 按半径同比例计算。</param>
        /// <exception cref="ArgumentOutOfRangeException">某个尺寸超出有限的支持区间。</exception>
        public PipeGeometryProfile(
            float radius = 0.32f,
            float centerHeight = 0.34f,
            float fittingScale = 1f,
            float supportRingWidth = 0f,
            float? isolatedHalfLength = null
        )
        {
            RequireRange(radius, 0.08f, 2f, nameof(radius));
            RequireRange(centerHeight, 0, 2, nameof(centerHeight));
            RequireRange(fittingScale, 0.75f, 1.5f, nameof(fittingScale));
            RequireRange(supportRingWidth, 0, radius, nameof(supportRingWidth));
            float halfLength = isolatedHalfLength ?? (0.35f * radius / 0.32f);
            RequireRange(halfLength, 0.05f, 4f, nameof(isolatedHalfLength));
            Radius = radius;
            CenterHeight = centerHeight;
            FittingScale = fittingScale;
            SupportRingWidth = supportRingWidth;
            IsolatedHalfLength = halfLength;
        }

        private static void RequireRange(
            float value,
            float minimum,
            float maximum,
            string parameter
        )
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < minimum || value > maximum)
            {
                throw new ArgumentOutOfRangeException(
                    parameter,
                    value,
                    "A finite dimension within the supported interval is required."
                );
            }
        }
    }
}
