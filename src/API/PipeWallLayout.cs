using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace FluidPipes.API
{
    /// <summary>围绕管道截面的角向表面带。</summary>
    public sealed class PipeWallBand
    {
        /// <summary>表面带中心相对截面上方的角度，单位为度。</summary>
        public float CenterDegrees { get; }

        /// <summary>水平段上的完整角宽，范围为 0–360 度。</summary>
        public float WidthDegrees { get; }

        /// <summary>竖直段上的完整角宽，范围为 0–360 度。</summary>
        public float VerticalWidthDegrees { get; }

        /// <summary>表面带使用的玻璃管壁或不透明接头材质。</summary>
        public PipeSurfaceRole Surface { get; }

        /// <summary>定义随管道倾斜程度改变宽度的角向表面带。</summary>
        /// <param name="centerDegrees">相对截面上方的中心角，单位为度。</param>
        /// <param name="widthDegrees">水平段完整角宽，范围为 0–360 度。</param>
        /// <param name="verticalWidthDegrees">竖直段完整角宽；null 使用水平角宽。</param>
        /// <param name="surface">Wall 使用玻璃材质，Fittings 使用不透明材质。</param>
        /// <exception cref="ArgumentOutOfRangeException">角度非有限、角宽越界或表面角色无效。</exception>
        public PipeWallBand(
            float centerDegrees,
            float widthDegrees,
            float? verticalWidthDegrees = null,
            PipeSurfaceRole surface = PipeSurfaceRole.Wall
        )
        {
            RequireFinite(centerDegrees, nameof(centerDegrees));
            RequireWidth(widthDegrees, nameof(widthDegrees));
            RequireWidth(verticalWidthDegrees ?? widthDegrees, nameof(verticalWidthDegrees));
            RequireSurface(surface, nameof(surface));
            CenterDegrees = Mathf.DeltaAngle(0, centerDegrees);
            WidthDegrees = widthDegrees;
            VerticalWidthDegrees = verticalWidthDegrees ?? widthDegrees;
            Surface = surface;
        }

        internal static void RequireSurface(PipeSurfaceRole surface, string parameter)
        {
            if (surface != PipeSurfaceRole.Wall && surface != PipeSurfaceRole.Fittings)
            {
                throw new ArgumentOutOfRangeException(parameter);
            }
        }

        private static void RequireWidth(float width, string parameter)
        {
            RequireFinite(width, parameter);
            if (width < 0 || width > 360)
            {
                throw new ArgumentOutOfRangeException(parameter);
            }
        }

        private static void RequireFinite(float value, string parameter)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(parameter);
            }
        }
    }

    /// <summary>按截面角度为管壁分配玻璃和不透明表面的不可变布局。</summary>
    public sealed class PipeWallLayout
    {
        /// <summary>按优先顺序排列的表面带；后面的重叠带覆盖前面的带。</summary>
        public IReadOnlyList<PipeWallBand> Bands { get; }

        /// <summary>表面带外的材质角色。</summary>
        public PipeSurfaceRole BackgroundSurface { get; }

        /// <summary>开始向竖直布局过渡时的管道仰角，单位为度。</summary>
        public float TransitionStartDegrees { get; }

        /// <summary>完全使用竖直布局时的管道仰角，单位为度。</summary>
        public float TransitionEndDegrees { get; }

        internal bool IsReversalSymmetric { get; }

        /// <summary>定义管壁分区，水平参考径向上方，竖直参考行星北向。</summary>
        /// <param name="bands">1–16 个角向表面带，构造时复制集合。</param>
        /// <param name="backgroundSurface">表面带外的角色，默认为不透明接头材质。</param>
        /// <param name="transitionStartDegrees">开始过渡的仰角，范围为 0–90 度。</param>
        /// <param name="transitionEndDegrees">结束过渡的仰角，必须大于开始角且不大于 90 度。</param>
        /// <exception cref="ArgumentNullException">集合或其中的表面带为 null。</exception>
        /// <exception cref="ArgumentOutOfRangeException">表面带数量、仰角或背景角色越界。</exception>
        public PipeWallLayout(
            IEnumerable<PipeWallBand> bands,
            PipeSurfaceRole backgroundSurface = PipeSurfaceRole.Fittings,
            float transitionStartDegrees = 60,
            float transitionEndDegrees = 80
        )
        {
            if (bands == null)
            {
                throw new ArgumentNullException(nameof(bands));
            }
            var copy = new List<PipeWallBand>();
            foreach (var band in bands)
            {
                if (band == null)
                {
                    throw new ArgumentNullException(nameof(bands));
                }
                copy.Add(band);
                if (copy.Count > 16)
                {
                    throw new ArgumentOutOfRangeException(nameof(bands));
                }
            }
            if (copy.Count == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bands));
            }
            PipeWallBand.RequireSurface(backgroundSurface, nameof(backgroundSurface));
            if (!(transitionStartDegrees >= 0 && transitionStartDegrees < 90))
            {
                throw new ArgumentOutOfRangeException(nameof(transitionStartDegrees));
            }
            if (!(transitionEndDegrees > transitionStartDegrees && transitionEndDegrees <= 90))
            {
                throw new ArgumentOutOfRangeException(nameof(transitionEndDegrees));
            }
            Bands = new ReadOnlyCollection<PipeWallBand>(copy);
            BackgroundSurface = backgroundSurface;
            TransitionStartDegrees = transitionStartDegrees;
            TransitionEndDegrees = transitionEndDegrees;
            IsReversalSymmetric = true;
            for (int index = 0; index < copy.Count; index++)
            {
                var band = copy[index];
                if (Mathf.Abs(Mathf.DeltaAngle(band.CenterDegrees, -band.CenterDegrees)) < 0.0001f)
                {
                    continue;
                }
                if (index + 1 >= copy.Count || !Mirrors(band, copy[index + 1]))
                {
                    IsReversalSymmetric = false;
                    break;
                }
                index++;
            }
        }

        private static bool Mirrors(PipeWallBand first, PipeWallBand second) =>
            Mathf.Abs(Mathf.DeltaAngle(first.CenterDegrees, -second.CenterDegrees)) < 0.0001f
            && first.WidthDegrees == second.WidthDegrees
            && first.VerticalWidthDegrees == second.VerticalWidthDegrees
            && first.Surface == second.Surface;

        internal PipeSurfaceRole Resolve(Vector3 normal, Vector3 tangent, Vector3 radial)
        {
            tangent.Normalize();
            radial.Normalize();
            float elevation =
                Mathf.Asin(Mathf.Clamp01(Mathf.Abs(Vector3.Dot(tangent, radial)))) * Mathf.Rad2Deg;
            float blend = Mathf.SmoothStep(
                0,
                1,
                Mathf.InverseLerp(TransitionStartDegrees, TransitionEndDegrees, elevation)
            );
            Vector3 north = Vector3.ProjectOnPlane(Vector3.up, tangent);
            if (north.sqrMagnitude < 0.0001f)
            {
                north = Vector3.ProjectOnPlane(Vector3.right, tangent);
            }
            north.Normalize();
            Vector3 upper = Vector3.ProjectOnPlane(radial, tangent);
            if (upper.sqrMagnitude < 0.0001f)
            {
                upper = north;
            }
            upper.Normalize();
            if (Vector3.Dot(upper, north) < 0)
            {
                north = -north;
            }
            Vector3 reference = Vector3.Slerp(upper, north, blend).normalized;
            Vector3 side = Vector3.Cross(tangent, reference).normalized;
            float angle =
                Mathf.Atan2(Vector3.Dot(normal, side), Vector3.Dot(normal, reference))
                * Mathf.Rad2Deg;
            PipeSurfaceRole surface = BackgroundSurface;
            foreach (var band in Bands)
            {
                float width = Mathf.Lerp(band.WidthDegrees, band.VerticalWidthDegrees, blend);
                if (
                    width > 0
                    && Mathf.Abs(Mathf.DeltaAngle(angle, band.CenterDegrees)) <= width * 0.5f
                )
                {
                    surface = band.Surface;
                }
            }
            return surface;
        }
    }
}
