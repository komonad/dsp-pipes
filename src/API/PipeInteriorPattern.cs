using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace FluidPipes.API
{
    /// <summary>周期性纵向构件在一个相位上的径向偏移和截面倍率。</summary>
    public sealed class PipeInteriorKeyframe
    {
        /// <summary>一个周期内的相位，范围为 0 到 1。</summary>
        public float Phase { get; }

        /// <summary>附加径向偏移与外管壁半径之比。</summary>
        public float RadialOffset { get; }

        /// <summary>截面尺寸倍率。实心截面的零倍率收拢到中心；圆环截面的倍率控制壁厚，内孔半径保持固定。</summary>
        public float Scale { get; }

        /// <summary>定义周期构件的一个不可变关键帧。</summary>
        /// <param name="phase">有限的相位，范围为 0 到 1。</param>
        /// <param name="radialOffset">有限的径向偏移比例，范围为 -0.85 到 0.85。</param>
        /// <param name="scale">有限的截面倍率，范围为 0 到 2。</param>
        /// <exception cref="ArgumentOutOfRangeException">某个值超出支持范围。</exception>
        public PipeInteriorKeyframe(float phase, float radialOffset = 0, float scale = 1)
        {
            RequireRange(phase, 0, 1, nameof(phase));
            RequireRange(radialOffset, -0.85f, 0.85f, nameof(radialOffset));
            RequireRange(scale, 0, 2, nameof(scale));
            Phase = phase;
            RadialOffset = radialOffset;
            Scale = scale;
        }

        private static void RequireRange(float value, float minimum, float maximum, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < minimum || value > maximum)
            {
                throw new ArgumentOutOfRangeException(name);
            }
        }
    }

    /// <summary>沿世界路径重复的斜撑、套节或设施支架的不可变关键帧模式。</summary>
    /// <remarks>相位回调在网格重建期间执行，并接收工厂和中心线的世界位置。相邻路径采样点之间的相位跨度应小于半个周期。关键帧之间线性插值，网格在经过的关键帧处补充截面。回调在相同位置返回相同相位，使相邻节点共享边界形状。</remarks>
    public sealed class PipeInteriorPattern
    {
        private readonly Func<PlanetFactory, Vector3, float> phase;

        /// <summary>按相位递增排列的只读关键帧。</summary>
        public IReadOnlyList<PipeInteriorKeyframe> Keyframes { get; }

        /// <summary>局部纵向纹理窗口的起始相位。</summary>
        public float TextureStartPhase { get; }

        /// <summary>覆盖一张纵向纹理的相位跨度。零表示使用外观的世界纹理坐标。</summary>
        public float TexturePhaseSpan { get; }

        /// <summary>定义从相位零到相位一的闭合周期。</summary>
        /// <param name="phase">从工厂和中心线世界位置计算周期相位的纯函数；返回值的整数部分表示已完成的周期。</param>
        /// <param name="keyframes">2 到 32 个关键帧，首尾相位分别为 0 和 1，首尾偏移与倍率相同。</param>
        /// <exception cref="ArgumentNullException">回调、集合或关键帧为 null。</exception>
        /// <exception cref="ArgumentException">关键帧数量、顺序或周期边界无效。</exception>
        public PipeInteriorPattern(
            Func<PlanetFactory, Vector3, float> phase,
            IEnumerable<PipeInteriorKeyframe> keyframes
        )
            : this(phase, keyframes, 0, 0) { }

        private PipeInteriorPattern(
            Func<PlanetFactory, Vector3, float> phase,
            IEnumerable<PipeInteriorKeyframe> keyframes,
            float textureStartPhase,
            float texturePhaseSpan
        )
        {
            TextureStartPhase = textureStartPhase;
            TexturePhaseSpan = texturePhaseSpan;
            this.phase = phase ?? throw new ArgumentNullException(nameof(phase));
            if (keyframes == null)
            {
                throw new ArgumentNullException(nameof(keyframes));
            }
            var snapshot = new List<PipeInteriorKeyframe>();
            foreach (var keyframe in keyframes)
            {
                if (keyframe == null)
                {
                    throw new ArgumentNullException(nameof(keyframes));
                }
                if (
                    snapshot.Count >= 32
                    || snapshot.Count > 0 && keyframe.Phase <= snapshot[snapshot.Count - 1].Phase
                )
                {
                    throw new ArgumentException(
                        "Keyframes require increasing phases and at most 32 entries.",
                        nameof(keyframes)
                    );
                }
                snapshot.Add(keyframe);
            }
            if (
                snapshot.Count < 2
                || snapshot[0].Phase != 0
                || snapshot[snapshot.Count - 1].Phase != 1
                || snapshot[0].RadialOffset != snapshot[snapshot.Count - 1].RadialOffset
                || snapshot[0].Scale != snapshot[snapshot.Count - 1].Scale
            )
            {
                throw new ArgumentException(
                    "A period requires matching endpoints at phases zero and one.",
                    nameof(keyframes)
                );
            }
            Keyframes = new ReadOnlyCollection<PipeInteriorKeyframe>(snapshot);
        }

        /// <summary>返回将整张纵向纹理集中到指定相位区间的模式副本。</summary>
        /// <param name="startPhase">有限的起始相位，按一个周期归一化。</param>
        /// <param name="phaseSpan">纹理窗口跨度，范围为 0.001 到 1。</param>
        /// <returns>共享相位回调、保留关键帧的不可变模式。</returns>
        /// <exception cref="ArgumentOutOfRangeException">参数为非有限值或跨度超出支持范围。</exception>
        public PipeInteriorPattern WithTextureWindow(float startPhase, float phaseSpan)
        {
            if (float.IsNaN(startPhase) || float.IsInfinity(startPhase))
            {
                throw new ArgumentOutOfRangeException(nameof(startPhase));
            }
            if (
                float.IsNaN(phaseSpan)
                || float.IsInfinity(phaseSpan)
                || phaseSpan < 0.001f
                || phaseSpan > 1
            )
            {
                throw new ArgumentOutOfRangeException(nameof(phaseSpan));
            }
            return new PipeInteriorPattern(
                phase,
                Keyframes,
                Mathf.Repeat(startPhase, 1),
                phaseSpan
            );
        }

        internal float PhaseAt(PlanetFactory factory, Vector3 position)
        {
            float value = phase(factory, position);
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new InvalidOperationException("Interior pattern phases must be finite.");
            }
            return Mathf.Repeat(value, 1);
        }

        internal void Sample(float phaseValue, out float radialOffset, out float scale)
        {
            float value = Mathf.Repeat(phaseValue, 1);
            for (int index = 1; index < Keyframes.Count; index++)
            {
                var next = Keyframes[index];
                if (value > next.Phase)
                {
                    continue;
                }
                var previous = Keyframes[index - 1];
                float fraction = Mathf.InverseLerp(previous.Phase, next.Phase, value);
                radialOffset = Mathf.Lerp(previous.RadialOffset, next.RadialOffset, fraction);
                scale = Mathf.Lerp(previous.Scale, next.Scale, fraction);
                return;
            }
            radialOffset = Keyframes[0].RadialOffset;
            scale = Keyframes[0].Scale;
        }
    }
}
