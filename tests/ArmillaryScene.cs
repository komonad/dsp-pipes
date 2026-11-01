using System;
using UnityEngine;

public sealed partial class SceneCapture
{
    private void CreateArmillary()
    {
        const float height = 23;
        var centerLocal = Vector3.up * height;
        var axis = new Vector3(0, Mathf.Sin(35 * Mathf.Deg2Rad), Mathf.Cos(35 * Mathf.Deg2Rad));
        var equatorial = Vector3.Cross(axis, Vector3.right).normalized;
        armillaryFrame = NewSolidPiece("armillary", 0, 0, 0);
        armillaryFrame.Center = armillaryFrame.Origin + armillaryFrame.Up * 20;
        armillaryFrame.Radius = 23;

        var frame = NewSolidPiece("armillary-frame", 0, 0, 1114);
        int[] horizon = AddInstrumentCurve(
            frame,
            144,
            true,
            t =>
                centerLocal
                + new Vector3(
                    16.7f * Mathf.Cos(t * 2 * Mathf.PI),
                    0,
                    16.7f * Mathf.Sin(t * 2 * Mathf.PI)
                )
        );
        int[][] meridians = new int[2][];
        for (int side = 0; side < 2; side++)
        {
            float offset = side == 0 ? -0.7f : 0.7f;
            meridians[side] = AddInstrumentCurve(
                frame,
                144,
                true,
                t =>
                    centerLocal
                    + new Vector3(
                        offset,
                        15.5f * Mathf.Sin(t * 2 * Mathf.PI),
                        15.5f * Mathf.Cos(t * 2 * Mathf.PI)
                    )
            );
            AddInstrumentSpan(frame, meridians[side][0], horizon[36]);
            AddInstrumentSpan(frame, meridians[side][72], horizon[108]);
        }
        foreach (int index in new[] { 14, 36, 58, 86, 108, 130 })
            AddInstrumentSpan(frame, meridians[0][index], meridians[1][index]);

        var corners = new int[4];
        var upperCorners = new int[4];
        for (int corner = 0; corner < 4; corner++)
        {
            float a = (45 + corner * 90) * Mathf.Deg2Rad;
            Vector3 foot = new Vector3(16.7f * Mathf.Cos(a), 1, 16.7f * Mathf.Sin(a));
            corners[corner] = InstrumentNode(frame, foot);
            upperCorners[corner] = InstrumentNode(frame, foot + Vector3.up * 2);
            AddInstrumentSpan(frame, corners[corner], upperCorners[corner]);
            int top = horizon[18 + corner * 36];
            AddInstrumentCurve(
                frame,
                26,
                false,
                t =>
                    Vector3.Lerp(foot + Vector3.up * 2, InstrumentLocal(frame, top), t)
                    + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (1.5f * Mathf.Sin(t * Mathf.PI)),
                upperCorners[corner],
                top
            );
        }
        for (int corner = 0; corner < 4; corner++)
        {
            int next = (corner + 1) % 4;
            AddInstrumentSpan(frame, corners[corner], corners[next]);
            AddInstrumentSpan(frame, upperCorners[corner], upperCorners[next]);
        }
        int poleSouth = InstrumentNode(frame, centerLocal - axis * 15.5f);
        int poleNorth = InstrumentNode(frame, centerLocal + axis * 15.5f);
        AddInstrumentSpan(frame, poleSouth, poleNorth);
        AddInstrumentSpan(frame, poleSouth, meridians[0][86]);
        AddInstrumentSpan(frame, poleSouth, meridians[1][86]);
        AddInstrumentSpan(frame, poleNorth, meridians[0][14]);
        AddInstrumentSpan(frame, poleNorth, meridians[1][14]);
        CompleteInstrument(frame);

        var equator = NewSolidPiece("armillary-equator", 0, 0, 1000);
        var equatorBands = new int[2][];
        for (int band = 0; band < 2; band++)
        {
            float offset = band == 0 ? -0.45f : 0.45f;
            equatorBands[band] = AddInstrumentCurve(
                equator,
                128,
                true,
                t =>
                    centerLocal
                    + axis * offset
                    + (
                        Vector3.right * Mathf.Cos(t * 2 * Mathf.PI)
                        + equatorial * Mathf.Sin(t * 2 * Mathf.PI)
                    ) * 12.6f
            );
        }
        foreach (int index in new[] { 0, 32, 64, 96 })
            AddInstrumentSpan(equator, equatorBands[0][index], equatorBands[1][index]);
        CompleteInstrument(equator);

        var ecliptic = NewSolidPiece("armillary-ecliptic", 0, 0, 1116);
        Vector3 eclipticNormal = Quaternion.AngleAxis(23.4f, Vector3.right) * axis;
        Vector3 eclipticSide = Vector3.Cross(eclipticNormal, Vector3.right).normalized;
        AddInstrumentCurve(
            ecliptic,
            128,
            true,
            t =>
                centerLocal
                + (
                    Vector3.right * Mathf.Cos(t * 2 * Mathf.PI)
                    + eclipticSide * Mathf.Sin(t * 2 * Mathf.PI)
                ) * 11.4f
        );
        CompleteInstrument(ecliptic);

        var sight = NewSolidPiece("armillary-sighting", 0, 0, 1121);
        Vector3 sightSide = Quaternion.AngleAxis(40, axis) * Vector3.right;
        Vector3 sightNormal = Vector3.Cross(sightSide, axis).normalized;
        var sightBands = new int[2][];
        for (int band = 0; band < 2; band++)
        {
            float offset = band == 0 ? -0.45f : 0.45f;
            sightBands[band] = AddInstrumentCurve(
                sight,
                112,
                true,
                t =>
                    centerLocal
                    + sightNormal * offset
                    + (sightSide * Mathf.Cos(t * 2 * Mathf.PI) + axis * Mathf.Sin(t * 2 * Mathf.PI))
                        * 9.6f
            );
        }
        foreach (int index in new[] { 0, 28, 56, 84 })
            AddInstrumentSpan(sight, sightBands[0][index], sightBands[1][index]);
        int sightStart = InstrumentNode(sight, centerLocal + sightSide * 9.6f);
        int sightEnd = InstrumentNode(sight, centerLocal - sightSide * 9.6f);
        AddInstrumentSpan(sight, sightStart, sightEnd);
        AddInstrumentSpan(sight, sightStart, sightBands[0][0]);
        AddInstrumentSpan(sight, sightStart, sightBands[1][0]);
        AddInstrumentSpan(sight, sightEnd, sightBands[0][56]);
        AddInstrumentSpan(sight, sightEnd, sightBands[1][56]);
        CompleteInstrument(sight);

        var globe = NewSolidPiece("armillary-globe", 0, 0, 1120);
        int[] globeEquator = AddInstrumentCurve(
            globe,
            64,
            true,
            t =>
                centerLocal
                + new Vector3(
                    2.8f * Mathf.Cos(t * 2 * Mathf.PI),
                    0,
                    2.8f * Mathf.Sin(t * 2 * Mathf.PI)
                )
        );
        int northPole = InstrumentNode(globe, centerLocal + Vector3.up * 2.8f);
        int southPole = InstrumentNode(globe, centerLocal - Vector3.up * 2.8f);
        for (int meridian = 0; meridian < 4; meridian++)
        {
            float a = meridian * Mathf.PI / 2;
            Vector3 direction = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            int equatorialNode = globeEquator[meridian * 16];
            AddInstrumentCurve(
                globe,
                16,
                false,
                t =>
                    centerLocal
                    + (
                        direction * Mathf.Sin(t * Mathf.PI / 2)
                        + Vector3.up * Mathf.Cos(t * Mathf.PI / 2)
                    ) * 2.8f,
                northPole,
                equatorialNode
            );
            AddInstrumentCurve(
                globe,
                16,
                false,
                t =>
                    centerLocal
                    + (
                        direction * Mathf.Cos(t * Mathf.PI / 2)
                        - Vector3.up * Mathf.Sin(t * Mathf.PI / 2)
                    ) * 2.8f,
                equatorialNode,
                southPole
            );
        }
        CompleteInstrument(globe);
        report.Add(
            "ARMILLARY horizon=horizontal meridian=double polar_elevation=35"
                + " equator=perpendicular_to_polar_axis ecliptic_obliquity=23.4"
                + " sighting_ring=double pedestal=four_pillars circuits=5"
        );
    }

    private void CompleteInstrument(SolidGalleryPiece piece)
    {
        FillSolid(piece);
        armillary.Add(piece);
    }

    private int[] AddInstrumentCurve(
        SolidGalleryPiece piece,
        int segments,
        bool closed,
        Func<float, Vector3> curve,
        int first = 0,
        int last = 0
    )
    {
        var nodes = new int[closed ? segments : segments + 1];
        for (int index = 0; index < nodes.Length; index++)
        {
            int node =
                index == 0 && first != 0 ? first
                : index == nodes.Length - 1 && last != 0 ? last
                : InstrumentNode(piece, curve((float)index / segments));
            nodes[index] = node;
            if (index > 0)
                ConnectSolidNodes(piece, nodes[index - 1], node);
        }
        if (closed)
            ConnectSolidNodes(piece, nodes[nodes.Length - 1], nodes[0]);
        return nodes;
    }

    private int InstrumentNode(SolidGalleryPiece piece, Vector3 local)
    {
        Vector3 position =
            piece.Origin + piece.East * local.x + piece.Up * local.y + piece.North * local.z;
        foreach (int node in piece.Nodes)
            if ((factory.entityPool[node].pos - position).sqrMagnitude < 0.000001f)
                return node;
        return AddSolidNode(piece, local);
    }

    private Vector3 InstrumentLocal(SolidGalleryPiece piece, int node)
    {
        Vector3 delta = factory.entityPool[node].pos - piece.Origin;
        return new Vector3(
            Vector3.Dot(delta, piece.East),
            Vector3.Dot(delta, piece.Up),
            Vector3.Dot(delta, piece.North)
        );
    }

    private void AddInstrumentSpan(SolidGalleryPiece piece, int first, int last)
    {
        Vector3 a = InstrumentLocal(piece, first);
        Vector3 b = InstrumentLocal(piece, last);
        AddInstrumentCurve(
            piece,
            Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / 0.8f)),
            false,
            t => Vector3.Lerp(a, b, t),
            first,
            last
        );
    }
}
