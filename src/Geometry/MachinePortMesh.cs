using UnityEngine;

namespace FluidPipes
{
    internal sealed partial class PipeMesh
    {
        internal const float MachinePortDepth = 1.05f;

        private readonly struct PortSection
        {
            public readonly float Depth;
            public readonly float Height;
            public readonly float HalfWidth;
            public readonly float HalfHeight;
            public readonly float Exponent;

            public PortSection(
                float depth,
                float height,
                float halfWidth,
                float halfHeight,
                float exponent = 12
            )
            {
                Depth = depth;
                Height = height;
                HalfWidth = halfWidth;
                HalfHeight = halfHeight;
                Exponent = exponent;
            }

            public Vector3 Point(int index)
            {
                float angle = index * Mathf.PI * 2 / Sides;
                float horizontal = Mathf.Cos(angle);
                float vertical = Mathf.Sin(angle);
                float distance = Mathf.Pow(
                    Mathf.Pow(Mathf.Abs(horizontal) / HalfWidth, Exponent)
                        + Mathf.Pow(Mathf.Abs(vertical) / HalfHeight, Exponent),
                    -1 / Exponent
                );
                return new Vector3(horizontal * distance, Height + vertical * distance, Depth);
            }
        }

        public void MachinePort(Vector3 anchor, Quaternion orientation, float outletHeight)
        {
            // Shared centers keep the casing opening, socket and adjoining pipe coaxial.
            float casingHalfWidth = Mathf.Max(0.42f, radius + 0.10f);
            float casingHalfHeight = Mathf.Max(0.45f, radius + 0.13f);
            float outletRimRadius = radius * (1 + 0.1f * geometry.FittingScale);
            var sections = new[]
            {
                new PortSection(-0.10f, outletHeight, casingHalfWidth, casingHalfHeight),
                new PortSection(0.60f, outletHeight, casingHalfWidth, casingHalfHeight),
                new PortSection(
                    0.65f,
                    outletHeight,
                    casingHalfWidth + 0.04f,
                    casingHalfHeight + 0.04f
                ),
                new PortSection(
                    0.77f,
                    outletHeight,
                    casingHalfWidth + 0.04f,
                    casingHalfHeight + 0.04f
                ),
                new PortSection(0.82f, outletHeight, casingHalfWidth, casingHalfHeight),
                new PortSection(0.82f, outletHeight, radius + 0.05f, radius + 0.08f),
                new PortSection(0.78f, outletHeight, radius + 0.04f, radius + 0.07f),
                new PortSection(0.78f, outletHeight, radius, radius, 2),
                new PortSection(0.88f, outletHeight, radius, radius, 2),
                new PortSection(0.92f, outletHeight, outletRimRadius, outletRimRadius, 2),
                new PortSection(1.01f, outletHeight, outletRimRadius, outletRimRadius, 2),
                new PortSection(MachinePortDepth, outletHeight, radius, radius, 2),
            };
            for (int sectionIndex = 0; sectionIndex + 1 < sections.Length; sectionIndex++)
            {
                int material =
                    sectionIndex == 6 ? MachinePanelMaterial
                    : sectionIndex >= 8 ? 1
                    : MachineShellMaterial;
                PortStrip(
                    anchor,
                    orientation,
                    sections[sectionIndex],
                    sections[sectionIndex + 1],
                    material
                );
            }

            Vector3 outward = orientation * Vector3.forward;
            Vector3 mouth = anchor + orientation * new Vector3(0, outletHeight, MachinePortDepth);
            Annulus(mouth, outward, vertices.Count - Sides, radius * 0.92f);
            PortBack(anchor, orientation, sections[0]);
            for (int horizontal = -1; horizontal <= 1; horizontal += 2)
            {
                for (int vertical = -1; vertical <= 1; vertical += 2)
                {
                    Vector3 fastener =
                        anchor
                        + orientation
                            * new Vector3(
                                horizontal * (casingHalfWidth - 0.05f),
                                outletHeight + vertical * (casingHalfHeight - 0.05f),
                                0.825f
                            );
                    PortFastener(fastener, orientation);
                }
            }
        }

        private void PortStrip(
            Vector3 anchor,
            Quaternion orientation,
            PortSection rear,
            PortSection front,
            int material
        )
        {
            int start = vertices.Count;
            for (int row = 0; row < 2; row++)
            {
                PortSection section = row == 0 ? rear : front;
                for (int index = 0; index < Sides; index++)
                {
                    Vector3 perimeter =
                        section.Point((index + 1) % Sides)
                        - section.Point((index + Sides - 1) % Sides);
                    Vector3 longitudinal = front.Point(index) - rear.Point(index);
                    vertices.Add(anchor + orientation * section.Point(index));
                    normals.Add(orientation * Vector3.Cross(perimeter, longitudinal).normalized);
                    uvs.Add(new Vector2((float)index / Sides, section.Depth));
                }
            }
            for (int index = 0; index < Sides; index++)
            {
                int next = (index + 1) % Sides;
                Triangle(material, start + index, start + next, start + Sides + index);
                Triangle(material, start + next, start + Sides + next, start + Sides + index);
            }
        }

        private void PortBack(Vector3 anchor, Quaternion orientation, PortSection section)
        {
            int start = vertices.Count;
            Vector3 normal = orientation * Vector3.back;
            vertices.Add(anchor + orientation * new Vector3(0, section.Height, section.Depth));
            normals.Add(normal);
            uvs.Add(Vector2.zero);
            for (int index = 0; index < Sides; index++)
            {
                vertices.Add(anchor + orientation * section.Point(index));
                normals.Add(normal);
                uvs.Add(Vector2.zero);
            }
            for (int index = 0; index < Sides; index++)
            {
                Triangle(
                    MachineShellMaterial,
                    start,
                    start + 1 + (index + 1) % Sides,
                    start + 1 + index
                );
            }
        }

        private void PortFastener(Vector3 center, Quaternion orientation)
        {
            Vector3 outward = orientation * Vector3.forward;
            Tube(
                new[] { center, center + outward * 0.012f, center + outward * 0.020f },
                orientation * Vector3.up,
                0.035f,
                1,
                false,
                true,
                new[] { 0.035f, 0.035f, 0.025f }
            );
        }
    }
}
