using System;
using UnityEngine;

namespace FluidPipes.API
{
    /// <summary>连接类型不可变的管壁、导体与效果设置。</summary>
    public sealed class PipeAppearance
    {
        /// <summary>管壁渲染类别。</summary>
        public PipeWallMode WallMode { get; }

        /// <summary>是否绘制装饰性内部几何。</summary>
        public bool HasCore { get; }

        /// <summary>默认单导体半径除以管壁半径。</summary>
        public float CoreRadiusFraction { get; }

        /// <summary>用于在相邻网格截面间展开纵向纹理坐标的周期。零表示坐标不具周期性。</summary>
        public float TextureCoordinatePeriod { get; }

        /// <summary>可选的结构截面。截面对象会启用内部渲染，并提供绘制在管壁内部的构件。</summary>
        public PipeInteriorProfile Interior { get; }

        internal Action<PipeMaterialSet> ConfigureMaterials { get; }
        internal Action<PipeEffectContext, MaterialPropertyBlock> ApplyEffects { get; }
        internal Func<Vector3, float> TextureCoordinate { get; }

        /// <summary>通过七参数二进制接口定义缓存材质和单导体效果。</summary>
        /// <param name="wallMode">管壁渲染类别。</param>
        /// <param name="configureMaterials">主线程回调，每轮资源生成时配置库自有材质副本。</param>
        /// <param name="hasCore">是否绘制装饰性导体。</param>
        /// <param name="coreRadiusFraction">导体半径比例，大于零且不大于 0.85。</param>
        /// <param name="applyEffects">主线程绘制回调，将批次统一变量写入借用的属性块；该属性块在本次回调期间有效。</param>
        /// <param name="textureCoordinate">将世界空间中心线位置映射为纵向纹理坐标的纯函数；null 使用局部弧长。</param>
        /// <param name="textureCoordinatePeriod">纹理坐标回调的有限非负周期。正值将相邻截面展开到最近的周期；零保留回调值。</param>
        /// <exception cref="ArgumentOutOfRangeException">管壁模式、内芯半径或坐标周期超出其支持范围。</exception>
        public PipeAppearance(
            PipeWallMode wallMode,
            Action<PipeMaterialSet> configureMaterials,
            bool hasCore,
            float coreRadiusFraction,
            Action<PipeEffectContext, MaterialPropertyBlock> applyEffects,
            Func<Vector3, float> textureCoordinate,
            float textureCoordinatePeriod
        )
            : this(
                wallMode,
                configureMaterials,
                hasCore,
                coreRadiusFraction,
                applyEffects,
                textureCoordinate,
                textureCoordinatePeriod,
                null
            ) { }

        /// <summary>定义缓存材质和可选的每批次效果。</summary>
        /// <param name="wallMode">管壁渲染类别。</param>
        /// <param name="configureMaterials">主线程回调，每轮资源生成时配置库自有材质副本。</param>
        /// <param name="hasCore">是否绘制装饰性导体。</param>
        /// <param name="coreRadiusFraction">导体半径比例，大于零且不大于 0.85。</param>
        /// <param name="applyEffects">主线程绘制回调，将批次统一变量写入借用的属性块；该属性块在本次回调期间有效。</param>
        /// <param name="textureCoordinate">将世界空间中心线位置映射为纵向纹理坐标的纯函数；null 使用局部弧长。</param>
        /// <param name="textureCoordinatePeriod">纹理坐标回调的有限非负周期。正值将各相邻截面展开到最近的周期；零保留回调值。局部弧长坐标保持其默认映射。</param>
        /// <param name="interior">启用内部渲染的结构截面。null 使用由 hasCore 和 coreRadiusFraction 控制的单导体。</param>
        /// <exception cref="ArgumentOutOfRangeException">管壁模式、内芯半径或坐标周期超出其支持范围。</exception>
        public PipeAppearance(
            PipeWallMode wallMode = PipeWallMode.Glass,
            Action<PipeMaterialSet> configureMaterials = null,
            bool hasCore = false,
            float coreRadiusFraction = 0.45f,
            Action<PipeEffectContext, MaterialPropertyBlock> applyEffects = null,
            Func<Vector3, float> textureCoordinate = null,
            float textureCoordinatePeriod = 0,
            PipeInteriorProfile interior = null
        )
        {
            if (!Enum.IsDefined(typeof(PipeWallMode), wallMode))
            {
                throw new ArgumentOutOfRangeException(nameof(wallMode));
            }

            if (
                float.IsNaN(coreRadiusFraction)
                || coreRadiusFraction <= 0
                || coreRadiusFraction > 0.85f
            )
            {
                throw new ArgumentOutOfRangeException(nameof(coreRadiusFraction));
            }

            if (
                float.IsNaN(textureCoordinatePeriod)
                || float.IsInfinity(textureCoordinatePeriod)
                || textureCoordinatePeriod < 0
            )
            {
                throw new ArgumentOutOfRangeException(nameof(textureCoordinatePeriod));
            }

            WallMode = wallMode;
            ConfigureMaterials = configureMaterials;
            HasCore = hasCore || interior != null;
            CoreRadiusFraction = coreRadiusFraction;
            ApplyEffects = applyEffects;
            TextureCoordinate = textureCoordinate;
            TextureCoordinatePeriod = textureCoordinatePeriod;
            Interior = interior;
        }
    }
}
