using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FluidPipes;
using UnityEngine;

public sealed partial class SceneCapture
{
    private sealed class SolidGalleryPiece
    {
        public string Name;
        public readonly List<int> Nodes = new List<int>();
        public readonly List<int> Corners = new List<int>();
        public readonly Dictionary<int, int> Degrees = new Dictionary<int, int>();
        public Vector3 Origin;
        public Vector3 Up;
        public Vector3 East;
        public Vector3 North;
        public Vector3 Center;
        public float Radius;
        public int Item;
        public int Edges;
        public int Stock;
    }

    private readonly List<SolidGalleryPiece> solidGallery = new List<SolidGalleryPiece>();
    private SolidGalleryPiece latticeGallery;

    private void CreateSolidsGallery()
    {
        float golden = (1 + Mathf.Sqrt(5)) / 2;
        var tetrahedron = new[]
        {
            new Vector3(1, 1, 1),
            new Vector3(1, -1, -1),
            new Vector3(-1, 1, -1),
            new Vector3(-1, -1, 1),
        };
        var cube = new List<Vector3>();
        var dodecahedron = new List<Vector3>();
        var icosahedron = new List<Vector3>();
        foreach (int x in new[] { -1, 1 })
        {
            foreach (int y in new[] { -1, 1 })
            {
                foreach (int z in new[] { -1, 1 })
                {
                    cube.Add(new Vector3(x, y, z));
                    dodecahedron.Add(new Vector3(x, y, z));
                }
                dodecahedron.Add(new Vector3(0, x / golden, y * golden));
                dodecahedron.Add(new Vector3(x / golden, y * golden, 0));
                dodecahedron.Add(new Vector3(x * golden, 0, y / golden));
                icosahedron.Add(new Vector3(0, x, y * golden));
                icosahedron.Add(new Vector3(x, y * golden, 0));
                icosahedron.Add(new Vector3(x * golden, 0, y));
            }
        }
        var octahedron = new[]
        {
            Vector3.right,
            Vector3.left,
            Vector3.up,
            Vector3.down,
            Vector3.forward,
            Vector3.back,
        };

        solidGallery.Add(
            BuildRegularSolid(
                "tetrahedron",
                -24,
                14,
                tetrahedron,
                8,
                6,
                3,
                1114,
                Quaternion.Euler(0, 12, 0)
                    * Quaternion.FromToRotation(tetrahedron[0].normalized, Vector3.up)
            )
        );
        solidGallery.Add(
            BuildRegularSolid("cube", 0, 14, cube.ToArray(), 7, 12, 3, 1000, Quaternion.identity)
        );
        solidGallery.Add(
            BuildRegularSolid(
                "octahedron",
                24,
                14,
                octahedron,
                7,
                12,
                4,
                1007,
                Quaternion.Euler(0, 18, 0)
            )
        );
        solidGallery.Add(
            BuildRegularSolid(
                "dodecahedron",
                -13,
                -12,
                dodecahedron.ToArray(),
                6,
                30,
                3,
                1116,
                Quaternion.Euler(12, 14, 8)
            )
        );
        solidGallery.Add(
            BuildRegularSolid(
                "icosahedron",
                13,
                -12,
                icosahedron.ToArray(),
                7,
                30,
                5,
                1121,
                Quaternion.Euler(0, 14, 0)
            )
        );
        latticeGallery = BuildCubicLattice(0, -47);
        foreach (var piece in solidGallery)
        {
            ValidateSolidInventory(piece);
        }
        ValidateSolidInventory(latticeGallery);
        PrepareShowcase();
        SetView(0, 2, 62, 40, 8, 5);
    }

    private SolidGalleryPiece NewSolidPiece(string name, float x, float z, int item)
    {
        Vector3 origin = Point(x, z, 0.12f);
        Vector3 radial = origin.normalized;
        Vector3 horizontal = Vector3.ProjectOnPlane(east, radial).normalized;
        return new SolidGalleryPiece
        {
            Name = name,
            Origin = origin,
            Up = radial,
            East = horizontal,
            North = Vector3.Cross(horizontal, radial).normalized,
            Item = item,
        };
    }

    private SolidGalleryPiece BuildRegularSolid(
        string name,
        float x,
        float z,
        Vector3[] vertices,
        float edgeLength,
        int expectedEdges,
        int expectedDegree,
        int item,
        Quaternion rotation
    )
    {
        var piece = NewSolidPiece(name, x, z, item);
        float shortest = float.MaxValue;
        for (int first = 0; first < vertices.Length; first++)
        {
            for (int second = first + 1; second < vertices.Length; second++)
            {
                shortest = Mathf.Min(shortest, Vector3.Distance(vertices[first], vertices[second]));
            }
        }
        var positions = vertices
            .Select(vertex => rotation * vertex * (edgeLength / shortest))
            .ToArray();
        float bottom = positions.Min(position => position.y);
        float top = positions.Max(position => position.y);
        for (int index = 0; index < positions.Length; index++)
        {
            positions[index] -= Vector3.up * bottom;
            piece.Corners.Add(AddSolidNode(piece, positions[index]));
        }
        piece.Center = piece.Origin + piece.Up * ((top - bottom) / 2 + 0.34f);
        piece.Radius = positions.Max(position =>
            (position - Vector3.up * ((top - bottom) / 2)).magnitude
        );
        for (int first = 0; first < positions.Length; first++)
        {
            for (int second = first + 1; second < positions.Length; second++)
            {
                if (
                    Mathf.Abs(Vector3.Distance(positions[first], positions[second]) - edgeLength)
                    > 0.001f
                )
                {
                    continue;
                }
                int previous = piece.Corners[first];
                int steps = Mathf.CeilToInt(edgeLength / 0.9f);
                for (int step = 1; step <= steps; step++)
                {
                    int node =
                        step == steps
                            ? piece.Corners[second]
                            : AddSolidNode(
                                piece,
                                Vector3.Lerp(
                                    positions[first],
                                    positions[second],
                                    (float)step / steps
                                )
                            );
                    ConnectSolidNodes(piece, previous, node);
                    previous = node;
                }
                piece.Edges++;
            }
        }
        if (
            piece.Edges != expectedEdges
            || piece.Corners.Any(node => piece.Degrees[node] != expectedDegree)
        )
        {
            throw new InvalidOperationException(
                "Regular-solid corner or edge count is invalid: " + name
            );
        }
        report.Add(
            "SOLID "
                + name
                + " vertices="
                + vertices.Length
                + " edges="
                + piece.Edges
                + " edge_length="
                + edgeLength
                + " pipe_nodes="
                + piece.Nodes.Count
                + " fluid="
                + item
        );
        FillSolid(piece);
        return piece;
    }

    private SolidGalleryPiece BuildCubicLattice(float x, float z)
    {
        const int cells = 4;
        const int width = 3;
        var piece = NewSolidPiece("lattice", x, z, 1000);
        var intersections = new Dictionary<Vector3Int, int>();
        var points = new Dictionary<Vector3Int, int>();
        for (int ix = 0; ix <= cells; ix++)
        {
            for (int iy = 0; iy <= cells; iy++)
            {
                for (int iz = 0; iz <= cells; iz++)
                {
                    var key = new Vector3Int(ix * width, iy * width, iz * width);
                    int node = AddLatticeNode(piece, points, key);
                    intersections.Add(key, node);
                    piece.Corners.Add(node);
                }
            }
        }
        foreach (var intersection in intersections)
        {
            foreach (var axis in new[] { Vector3Int.right, Vector3Int.up, new Vector3Int(0, 0, 1) })
            {
                var endpoint = intersection.Key + axis * width;
                if (!intersections.ContainsKey(endpoint))
                {
                    continue;
                }
                int previous = intersection.Value;
                for (int step = 1; step <= width; step++)
                {
                    int node = AddLatticeNode(piece, points, intersection.Key + axis * step);
                    ConnectSolidNodes(piece, previous, node);
                    previous = node;
                }
                piece.Edges++;
            }
        }
        if (intersections.Count != 125 || piece.Edges != 300 || piece.Nodes.Count != 725)
        {
            throw new InvalidOperationException(
                "Cubic-lattice dimensions or sample counts are invalid."
            );
        }
        foreach (var intersection in intersections)
        {
            var p = intersection.Key;
            int expected =
                (p.x == 0 || p.x == 12 ? 1 : 2)
                + (p.y == 0 || p.y == 12 ? 1 : 2)
                + (p.z == 0 || p.z == 12 ? 1 : 2);
            if (piece.Degrees[intersection.Value] != expected)
            {
                throw new InvalidOperationException(
                    "Cubic-lattice intersection has an invalid degree."
                );
            }
        }
        piece.Center = piece.Origin + piece.Up * 6.34f;
        piece.Radius = Mathf.Sqrt(108);
        report.Add(
            "LATTICE cells=4x4x4 cell_width=3 intersections=125 edges=300 pipe_nodes=725 segments=900 fluid=1000"
        );
        FillSolid(piece);
        return piece;
    }

    private int AddLatticeNode(
        SolidGalleryPiece piece,
        Dictionary<Vector3Int, int> points,
        Vector3Int key
    )
    {
        if (!points.TryGetValue(key, out int node))
        {
            node = AddSolidNode(piece, new Vector3(key.x - 6, key.y, key.z - 6));
            points.Add(key, node);
        }
        return node;
    }

    private int AddSolidNode(SolidGalleryPiece piece, Vector3 local)
    {
        Vector3 position =
            piece.Origin + piece.East * local.x + piece.Up * local.y + piece.North * local.z;
        int item = Prototypes.ItemId;
        int node = factory.AddEntityDataWithComponents(
            new EntityData
            {
                protoId = (short)item,
                modelIndex = (short)LDB.items.Select(item).ModelIndex,
                pos = position,
                rot = Quaternion.LookRotation(
                    Vector3.ProjectOnPlane(piece.North, position.normalized),
                    position.normalized
                ),
                alt = position.magnitude,
                localized = true,
            },
            0
        );
        piece.Nodes.Add(node);
        piece.Degrees.Add(node, 0);
        pipes++;
        return node;
    }

    private void ConnectSolidNodes(SolidGalleryPiece piece, int first, int second)
    {
        if (!PipeConnections.Connect(factory, first, second, 0, 1))
        {
            throw new InvalidOperationException("A solid edge cannot connect its pipe nodes.");
        }
        piece.Degrees[first]++;
        piece.Degrees[second]++;
    }

    private void FillSolid(SolidGalleryPiece piece)
    {
        piece.Stock = piece.Nodes.Count * 8;
        if (
            !PipeRuntime
                .Bind(factory.cargoTraffic)
                .Insert(piece.Nodes[0], piece.Item, piece.Stock, 0)
        )
        {
            throw new InvalidOperationException("A solid cannot receive its fluid inventory.");
        }
    }

    private void ValidateSolidInventory(SolidGalleryPiece piece)
    {
        var graph = PipeRuntime.Bind(factory.cargoTraffic);
        int degreeSum = 0;
        foreach (int node in piece.Nodes)
        {
            var view = graph.View(node);
            var links = graph.Links(node);
            if (
                view.Nodes != piece.Nodes.Count
                || view.Item != piece.Item
                || view.Count != piece.Stock
                || view.Capacity != piece.Nodes.Count * 10
                || view.ProliferatorPoints != 0
                || links.Length != piece.Degrees[node]
            )
            {
                throw new InvalidOperationException(
                    "A solid has inconsistent topology or fluid inventory: " + piece.Name
                );
            }
            degreeSum += links.Length;
        }
        var sample = graph.Take(piece.Nodes[piece.Nodes.Count - 1], 7, piece.Item);
        if (
            sample.Count != 7
            || sample.Item != piece.Item
            || !graph.Insert(
                piece.Nodes[0],
                sample.Item,
                (int)sample.Count,
                (int)sample.ProliferatorPoints
            )
            || graph.View(piece.Nodes[0]).Count != piece.Stock
        )
        {
            throw new InvalidOperationException(
                "Remote extraction does not conserve solid inventory."
            );
        }
        report.Add(
            "PASS "
                + piece.Name
                + " nodes="
                + piece.Nodes.Count
                + " segments="
                + degreeSum / 2
                + " shared_stock="
                + piece.Stock
                + " capacity="
                + piece.Nodes.Count * 10
                + " fluid="
                + piece.Item
        );
    }

    private void SetSolidView(
        SolidGalleryPiece piece,
        float azimuth,
        float elevation,
        float distance
    )
    {
        orthographicCapture = false;
        cameraUp = piece.Up;
        cameraTarget = piece.Center;
        float angle = azimuth * Mathf.Deg2Rad;
        float pitch = elevation * Mathf.Deg2Rad;
        cameraPosition =
            cameraTarget
            + (piece.East * Mathf.Sin(angle) - piece.North * Mathf.Cos(angle))
                * (Mathf.Cos(pitch) * distance)
            + piece.Up * (Mathf.Sin(pitch) * distance);
    }

    private IEnumerator CaptureSolidsGallery()
    {
        SaveImage("ingame-solids-gallery.png");
        float[] azimuths = { 35, 25, -35, 100, 225 };
        for (int index = 0; index < solidGallery.Count; index++)
        {
            var piece = solidGallery[index];
            SetSolidView(piece, azimuths[index], 32, piece.Radius * 3.7f);
            for (int frame = 0; frame < 12; frame++)
            {
                yield return null;
            }
            SaveImage("ingame-solids-" + piece.Name + ".png");
        }
        SetSolidView(solidGallery[3], 100, 17, solidGallery[3].Radius * 2.8f);
        for (int frame = 0; frame < 12; frame++)
        {
            yield return null;
        }
        SaveImage("ingame-solids-dodecahedron-detail.png");
        SetSolidView(solidGallery[4], 225, 18, solidGallery[4].Radius * 3.5f);
        for (int frame = 0; frame < 12; frame++)
        {
            yield return null;
        }
        SaveImage("ingame-solids-icosahedron-low.png");
        SetSolidView(latticeGallery, 145, 24, latticeGallery.Radius * 3.5f);
        for (int frame = 0; frame < 12; frame++)
        {
            yield return null;
        }
        SaveImage("ingame-solids-lattice.png");
        SetSolidView(latticeGallery, 150, 12, latticeGallery.Radius * 1.55f);
        for (int frame = 0; frame < 12; frame++)
        {
            yield return null;
        }
        SaveImage("ingame-solids-lattice-interior.png");
        SetSolidView(latticeGallery, 170, 65, latticeGallery.Radius * 3.2f);
        for (int frame = 0; frame < 12; frame++)
        {
            yield return null;
        }
        SaveImage("ingame-solids-lattice-top.png");
        foreach (var piece in solidGallery)
        {
            ValidateSolidInventory(piece);
        }
        ValidateSolidInventory(latticeGallery);
    }
}
