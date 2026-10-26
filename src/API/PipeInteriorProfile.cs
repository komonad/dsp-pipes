using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace FluidPipes.API
{
    /// <summary>A longitudinal member positioned inside the pipe envelope.</summary>
    public sealed class PipeInteriorMember
    {
        /// <summary>The cross-section offset divided by the outer wall radius. X follows the path tangent crossed with radial up; Y follows radial up projected onto the section plane.</summary>
        public Vector2 Offset { get; }

        /// <summary>The section's enclosing radius divided by the outer wall radius.</summary>
        public float RadiusFraction => Section.RadiusFraction;

        /// <summary>The immutable perimeter dimensions.</summary>
        public PipeInteriorSection Section { get; }

        /// <summary>The material and effect role used to draw this member.</summary>
        public PipeSurfaceRole Surface { get; }

        /// <summary>Defines a circular beam or conductor inside the wall envelope.</summary>
        /// <param name="offset">A finite normalized offset whose magnitude plus radiusFraction is strictly below 0.9.</param>
        /// <param name="radiusFraction">Member radius fraction, from 0.01 through 0.85.</param>
        /// <param name="surface">Core selects the conductor material; Fittings selects the metal fitting material.</param>
        /// <exception cref="ArgumentOutOfRangeException">An offset, radius or surface falls outside its supported range.</exception>
        public PipeInteriorMember(
            Vector2 offset,
            float radiusFraction,
            PipeSurfaceRole surface = PipeSurfaceRole.Core
        )
            : this(offset, PipeInteriorSection.Circle(radiusFraction), surface) { }

        /// <summary>Positions a configured section inside the pipe envelope.</summary>
        /// <param name="offset">Finite normalized lateral and radial offsets. Its magnitude plus the section's enclosing radius is strictly below 0.9.</param>
        /// <param name="section">The immutable perimeter shape and normalized dimensions.</param>
        /// <param name="surface">Core selects the conductor material; Fittings selects the metal fitting material.</param>
        /// <exception cref="ArgumentNullException">The section is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The offset or surface falls outside its supported range.</exception>
        public PipeInteriorMember(
            Vector2 offset,
            PipeInteriorSection section,
            PipeSurfaceRole surface = PipeSurfaceRole.Core
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
            if (surface != PipeSurfaceRole.Core && surface != PipeSurfaceRole.Fittings)
            {
                throw new ArgumentOutOfRangeException(nameof(surface));
            }
            Offset = offset;
            Section = section;
            Surface = surface;
        }
    }

    /// <summary>An immutable cross-section of longitudinal beams and conductors for structural pipes.</summary>
    /// <remarks>Undirected types match members one-to-one at (x, y) and (-x, y), including section shape, dimensions and surface role. Path reversal flips the lateral axis; the radial Y axis retains its orientation. Offsets and dimensions are measured relative to the outer wall radius. Members follow the node centerline; branch nodes generate one section per connected arm.</remarks>
    public sealed class PipeInteriorProfile
    {
        /// <summary>The ordered read-only snapshot of the configured members.</summary>
        public IReadOnlyList<PipeInteriorMember> Members { get; }

        internal bool IsReversalSymmetric { get; }

        /// <summary>Captures a bounded cross-section for procedural longitudinal geometry.</summary>
        /// <param name="members">From one through sixteen immutable members. Each member contributes an independently meshed section.</param>
        /// <exception cref="ArgumentNullException">The collection or one of its members is null.</exception>
        /// <exception cref="ArgumentException">The member count falls outside the supported interval.</exception>
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
                if (snapshot.Count > 16)
                {
                    throw new ArgumentException(
                        "A cross-section supports one through sixteen members.",
                        nameof(members)
                    );
                }
            }
            if (snapshot.Count == 0)
            {
                throw new ArgumentException(
                    "A cross-section supports one through sixteen members.",
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
                    && candidate.Section.Matches(source.Section)
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
