using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace FluidPipes.API
{
    /// <summary>位于管道包络内部的纵向构件。</summary>
    public sealed class PipeInteriorMember
    {
        /// <summary>截面偏移与外管壁半径之比。X 沿路径切线与径向向上方向的叉积，Y 沿径向向上方向在截面平面上的投影。</summary>
        public Vector2 Offset { get; }

        /// <summary>截面包围半径与外管壁半径之比。</summary>
        public float RadiusFraction => Section.RadiusFraction;

        /// <summary>不可变的周界尺寸。</summary>
        public PipeInteriorSection Section { get; }

        /// <summary>绘制该构件所用的材质和效果角色。</summary>
        public PipeSurfaceRole Surface { get; }

        /// <summary>沿世界路径重复的可选偏移与截面模式。</summary>
        public PipeInteriorPattern Pattern { get; }

        /// <summary>定义管壁包络内部的圆形梁或导体。</summary>
        /// <param name="offset">有限的归一化偏移，其模长加上 radiusFraction 严格小于 0.9。</param>
        /// <param name="radiusFraction">构件半径比例，范围为 0.01 到 0.85。</param>
        /// <param name="surface">Core 选择导体材质；Fittings 选择金属接头材质；Detail 选择结构附件材质。</param>
        /// <exception cref="ArgumentOutOfRangeException">偏移、半径或表面角色超出其支持范围。</exception>
        public PipeInteriorMember(
            Vector2 offset,
            float radiusFraction,
            PipeSurfaceRole surface = PipeSurfaceRole.Core
        )
            : this(offset, PipeInteriorSection.Circle(radiusFraction), surface) { }

        /// <summary>将已配置的截面放置在管道包络内部。</summary>
        /// <param name="offset">有限的归一化横向和径向偏移。其模长加上截面的包围半径严格小于 0.9。</param>
        /// <param name="section">不可变的周界形状和归一化尺寸。</param>
        /// <param name="surface">Core 选择导体材质；Fittings 选择金属接头材质；Detail 选择结构附件材质。</param>
        /// <exception cref="ArgumentNullException">截面为 null。</exception>
        /// <exception cref="ArgumentOutOfRangeException">偏移或表面角色超出其支持范围。</exception>
        public PipeInteriorMember(
            Vector2 offset,
            PipeInteriorSection section,
            PipeSurfaceRole surface = PipeSurfaceRole.Core
        )
            : this(offset, section, surface, null) { }

        /// <summary>将周期性截面构件放置在管道包络内部。</summary>
        /// <param name="offset">有限的归一化横向和径向偏移。</param>
        /// <param name="section">不可变的基础周界形状和尺寸。</param>
        /// <param name="surface">Core、Fittings 或 Detail 材质角色。</param>
        /// <param name="pattern">可选的周期模式；每个关键帧的偏移和截面均位于半径 0.9 的包络内。</param>
        /// <exception cref="ArgumentNullException">截面为 null。</exception>
        /// <exception cref="ArgumentOutOfRangeException">偏移、包络或表面角色超出支持范围。</exception>
        public PipeInteriorMember(
            Vector2 offset,
            PipeInteriorSection section,
            PipeSurfaceRole surface,
            PipeInteriorPattern pattern
        )
        {
            if (section == null)
            {
                throw new ArgumentNullException(nameof(section));
            }
            if (
                float.IsNaN(offset.x)
                || float.IsInfinity(offset.x)
                || float.IsNaN(offset.y)
                || float.IsInfinity(offset.y)
                || offset.magnitude + section.RadiusFraction >= 0.9f
            )
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }
            if (
                surface != PipeSurfaceRole.Core
                && surface != PipeSurfaceRole.Fittings
                && surface != PipeSurfaceRole.Detail
            )
            {
                throw new ArgumentOutOfRangeException(nameof(surface));
            }
            Offset = offset;
            Section = section;
            Surface = surface;
            Pattern = pattern;
            if (pattern != null)
            {
                foreach (var keyframe in pattern.Keyframes)
                {
                    var displaced = offset + Vector2.up * keyframe.RadialOffset;
                    if (displaced.magnitude + section.RadiusFraction * keyframe.Scale >= 0.9f)
                    {
                        throw new ArgumentOutOfRangeException(nameof(pattern));
                    }
                }
            }
        }
    }

    /// <summary>结构管道纵向梁和导体的不可变截面。</summary>
    /// <remarks>无向类型的构件在 (x, y) 与 (-x, y) 处一一匹配，包括截面形状、尺寸和表面角色。路径反向会翻转横向轴；径向 Y 轴保持其朝向。偏移和尺寸相对于外管壁半径度量。构件沿节点中心线延伸；分支节点为每个连接臂生成一个截面。</remarks>
    public sealed class PipeInteriorProfile
    {
        /// <summary>单个结构截面支持的最大构件数。</summary>
        public const int MaximumMembers = 16;

        /// <summary>已配置构件的有序只读快照。</summary>
        public IReadOnlyList<PipeInteriorMember> Members { get; }

        internal bool IsReversalSymmetric { get; }

        /// <summary>捕获用于程序化纵向几何的有界截面。</summary>
        /// <param name="members">1 到 16 个不可变构件。每个构件贡献一个独立网格化的截面。</param>
        /// <exception cref="ArgumentNullException">集合或其某个构件为 null。</exception>
        /// <exception cref="ArgumentException">构件数量超出支持区间。</exception>
        public PipeInteriorProfile(IEnumerable<PipeInteriorMember> members)
        {
            if (members == null)
            {
                throw new ArgumentNullException(nameof(members));
            }
            var snapshot = new List<PipeInteriorMember>();
            foreach (var member in members)
            {
                if (member == null)
                {
                    throw new ArgumentNullException(nameof(members));
                }
                snapshot.Add(member);
                if (snapshot.Count > MaximumMembers)
                {
                    throw new ArgumentException(
                        "An interior profile supports at most "
                            + MaximumMembers
                            + " members; received "
                            + snapshot.Count
                            + ".",
                        nameof(members)
                    );
                }
            }
            if (snapshot.Count == 0)
            {
                throw new ArgumentException(
                    "An interior profile requires at least one member.",
                    nameof(members)
                );
            }
            Members = new ReadOnlyCollection<PipeInteriorMember>(snapshot);
            IsReversalSymmetric = true;
            var matchedMembers = new bool[snapshot.Count];
            for (int memberIndex = 0; memberIndex < snapshot.Count; memberIndex++)
            {
                if (matchedMembers[memberIndex])
                {
                    continue;
                }
                int mirrorIndex = FindMirror(snapshot, matchedMembers, snapshot[memberIndex]);
                if (mirrorIndex < 0)
                {
                    IsReversalSymmetric = false;
                    break;
                }
                matchedMembers[memberIndex] = true;
                matchedMembers[mirrorIndex] = true;
            }
        }

        private static int FindMirror(
            List<PipeInteriorMember> members,
            bool[] matchedMembers,
            PipeInteriorMember source
        )
        {
            var offset = new Vector2(-source.Offset.x, source.Offset.y);
            for (int candidateIndex = 0; candidateIndex < members.Count; candidateIndex++)
            {
                var candidate = members[candidateIndex];
                if (
                    !matchedMembers[candidateIndex]
                    && candidate.Surface == source.Surface
                    && candidate.Section.MatchesMirror(source.Section)
                    && ReferenceEquals(candidate.Pattern, source.Pattern)
                    && (candidate.Offset - offset).sqrMagnitude <= 0.0000000001f
                )
                {
                    return candidateIndex;
                }
            }
            return -1;
        }
    }
}
