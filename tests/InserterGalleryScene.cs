using System;
using System.Collections;
using System.Collections.Generic;
using FluidPipes;
using UnityEngine;

public sealed partial class SceneCapture
{
    private sealed class GallerySorter
    {
        public int Entity;
        public int Source;
        public int Destination;
        public int Grade;
        public long InventoryTotal;
    }

    private sealed class GalleryHousingSorter
    {
        public int Entity;
        public int Pipe;
        public int Belt;
        public int Grade;
    }

    private readonly List<GallerySorter> gallerySorters = new List<GallerySorter>();
    private readonly List<GalleryHousingSorter> galleryHousingSorters =
        new List<GalleryHousingSorter>();
    private int galleryPowerNetwork;

    private void CreateInserterGallery()
    {
        int[] fluids = { 1000, 1114, 1116, 1120 };
        for (int grade = 1; grade <= 4; grade++)
        {
            var prototype = LDB.items.Select(2010 + grade);
            if (prototype == null || !prototype.prefabDesc.isInserter)
            {
                throw new InvalidOperationException("Inserter grade is not registered: " + grade);
            }
            if (!(LDB.items.Select(fluids[grade - 1])?.IsFluid ?? false))
            {
                throw new InvalidOperationException("Gallery liquid is not registered.");
            }

            for (int span = 1; span <= 3; span++)
            {
                AddGallerySorter(
                    grade,
                    span,
                    (grade - 2.5f) * 6.5f,
                    (span - 2) * 5.4f,
                    0,
                    fluids[grade - 1]
                );
            }
        }

        PrepareShowcase();
        SetView(0, 0, 30, 15, 5, 0.6f);
        orthographicCapture = true;
        captureOrthographicSize = 10.5f;
        report.Add("SORTER_GALLERY grades=2011,2012,2013,2014 spans=1,2,3 completed_inserters=12");
    }

    private void AddGallerySorter(
        int grade,
        int span,
        float x,
        float z,
        float destinationHeight,
        int fluid
    )
    {
        var source = Path(
            Prototypes.ItemId,
            x,
            z - span * 0.5f,
            new[] { V(-1, 0), V(0, 0), V(1, 0) },
            fluid
        );
        var destination = Path(
            Prototypes.ItemId,
            x,
            z + span * 0.5f,
            new[]
            {
                new Vector3(-1, destinationHeight, 0),
                new Vector3(0, destinationHeight, 0),
                new Vector3(1, destinationHeight, 0),
            },
            fluid
        );
        int entity = AddSceneSorter(
            source[1],
            destination[1],
            factory.entityPool[source[1]].pos,
            factory.entityPool[destination[1]].pos,
            true,
            2010 + grade
        );
        var graph = PipeRuntime.Bind(factory.cargoTraffic);
        gallerySorters.Add(
            new GallerySorter
            {
                Entity = entity,
                Source = source[1],
                Destination = destination[1],
                Grade = grade,
                InventoryTotal =
                    PipePools.View(graph, source[1]).Count
                    + PipePools.View(graph, destination[1]).Count,
            }
        );
        report.Add(
            "SORTER item="
                + (2010 + grade)
                + " span="
                + span
                + " height="
                + destinationHeight.ToString("F3")
                + " fluid="
                + fluid
                + " endpoint_distance="
                + (
                    factory.entityPool[destination[1]].pos - factory.entityPool[source[1]].pos
                ).magnitude.ToString("F3")
        );
    }

    private void ValidateGallerySorters()
    {
        var graph = PipeRuntime.Bind(factory.cargoTraffic);
        FluidInserterVisuals.Draw(factory, GameCamera.main);
        foreach (var sample in gallerySorters)
        {
            var data = factory.entityPool[sample.Entity];
            var arm = factory.factorySystem.inserterPool[data.inserterId];
            var source = PipePools.View(graph, sample.Source);
            var destination = PipePools.View(graph, sample.Destination);
            var native = SorterRenderer(sample.Entity).instPool[data.modelId];
            if (
                !FluidInserterVisuals.UsesPipe(factory, sample.Entity)
                || arm.grade != sample.Grade
                || data.powerConId <= 0
                || data.colliderId <= 0
                || native.objId == 0
                || native.posx != 0
                || native.posy != 0
                || native.posz != 0
                || PipePools.SameSpace(graph, sample.Source, sample.Destination)
                || source.Count + destination.Count + arm.itemCount != sample.InventoryTotal
            )
            {
                throw new InvalidOperationException(
                    "Gallery inserter loses its grade, display ownership or isolated inventory: "
                        + sample.Entity
                );
            }
        }
        if (FluidInserterVisuals.Count != gallerySorters.Count)
        {
            throw new InvalidOperationException("Gallery inserter display count is incomplete.");
        }
    }

    private void SetGalleryPower(float ratio)
    {
        var power = factory.powerSystem;
        if (galleryPowerNetwork == 0)
        {
            galleryPowerNetwork = power.NewNetwork();
        }
        foreach (var sample in gallerySorters)
        {
            int consumer = factory.entityPool[sample.Entity].powerConId;
            power.consumerPool[consumer].networkId = galleryPowerNetwork;
        }
        foreach (var sample in galleryHousingSorters)
        {
            int consumer = factory.entityPool[sample.Entity].powerConId;
            power.consumerPool[consumer].networkId = galleryPowerNetwork;
        }
        power.networkServes[galleryPowerNetwork] = ratio;
        foreach (var sample in gallerySorters)
        {
            if (Mathf.Abs(FluidInserterVisuals.PowerRatio(factory, sample.Entity) - ratio) > 0.001f)
            {
                throw new InvalidOperationException("Gallery supply does not reach its marker.");
            }
        }
        foreach (var sample in galleryHousingSorters)
        {
            if (Mathf.Abs(FluidInserterVisuals.PowerRatio(factory, sample.Entity) - ratio) > 0.001f)
            {
                throw new InvalidOperationException("Housing supply does not reach its marker.");
            }
        }
    }

    private IEnumerator CaptureInserterGallery()
    {
        ValidateGallerySorters();
        SetGalleryPower(1);
        SaveImage("ingame-sorters-overview.png");

        for (int grade = 1; grade <= 4; grade++)
        {
            SetView((grade - 2.5f) * 6.5f, 0.5f, 18, 0, 6, 0.7f);
            cameraUp = east;
            orthographicCapture = true;
            captureOrthographicSize = 4.3f;
            for (int frame = 0; frame < 12; frame++)
            {
                yield return null;
            }
            SetGalleryPower(1);
            SaveImage("ingame-sorters-grade-" + grade + "-lengths.png");
        }

        SetView(0, 5.4f, 18, 8, 0, 0.7f);
        for (int frame = 0; frame < 12; frame++)
        {
            yield return null;
        }
        SetGalleryPower(1);
        SaveImage("ingame-sorters-long-spans.png");

        SetView(9.75f, 5.4f, 3.5f, 5, -3.5f, 0.7f);
        for (int frame = 0; frame < 12; frame++)
        {
            yield return null;
        }
        int builds = FluidInserterVisuals.Rebuilds;
        SetGalleryPower(0);
        SaveImage("ingame-sorters-unpowered.png");
        SetGalleryPower(1);
        SaveImage("ingame-sorters-powered.png");
        if (FluidInserterVisuals.Rebuilds != builds)
        {
            throw new InvalidOperationException("Supply changes rebuild the gallery geometry.");
        }
        report.Add("POWER_MARKERS matched_camera supply=0,1 cached_geometry=true");

        for (int grade = 1; grade <= 4; grade++)
        {
            AddGallerySorter(grade, 3, (grade - 2.5f) * 4, -16, 1.3333333f, 1000);
        }
        SetView(0, -16, 10, -14, -2, 1);
        for (int frame = 0; frame < 24; frame++)
        {
            yield return null;
        }
        ValidateGallerySorters();
        SetGalleryPower(1);
        SaveImage("ingame-sorters-elevated.png");
        SetView(6, -16, 3.8f, -6, -3, 1.2f);
        for (int frame = 0; frame < 12; frame++)
        {
            yield return null;
        }
        SetGalleryPower(1);
        SaveImage("ingame-sorters-elevated-detail.png");
        report.Add(
            "PASS all four native inserter grades retain their components and isolated fluid spaces at three lengths and a raised endpoint."
        );

        var housings = CaptureGalleryHousings();
        while (housings.MoveNext())
        {
            yield return housings.Current;
        }
    }

    private void PrepareGalleryHousingStage()
    {
        var stage = Point(0, -39);
        var radial = stage.normalized;
        var forward = Vector3.ProjectOnPlane(north, radial).normalized;
        factory.FlattenTerrain(
            stage,
            Quaternion.LookRotation(forward, radial),
            new Bounds(Vector3.zero, new Vector3(30, 1, 20)),
            6,
            1,
            false,
            false,
            false,
            true
        );
        var data = factory.planet.data;
        for (int index = 1; index < data.vegeCursor; index++)
        {
            if (
                data.vegePool[index].id == index
                && (data.vegePool[index].pos - stage).sqrMagnitude < 900
            )
            {
                factory.RemoveVegeWithComponents(index);
            }
        }
        for (int index = 1; index < data.veinCursor; index++)
        {
            if (
                data.veinPool[index].id == index
                && (data.veinPool[index].pos - stage).sqrMagnitude < 900
            )
            {
                factory.RemoveVeinWithComponents(index);
            }
        }
    }

    private void AddGalleryHousingSorter(int grade, int span, float z)
    {
        float x = (grade - 2.5f) * 6;
        var source = Path(
            Prototypes.ItemId,
            x,
            z - span * 0.5f,
            new[] { V(-1, 0), V(0, 0), V(1, 0) },
            1000
        );
        var destination = Path(
            2001,
            x,
            z + span * 0.5f,
            new[] { V(-2, 0), V(-1, 0), V(0, 0), V(1, 0), V(2, 0) },
            0
        );
        int entity = AddSceneSorter(
            source[1],
            destination[2],
            factory.entityPool[source[1]].pos,
            factory.entityPool[destination[2]].pos,
            true,
            2010 + grade
        );
        galleryHousingSorters.Add(
            new GalleryHousingSorter
            {
                Entity = entity,
                Pipe = source[1],
                Belt = destination[2],
                Grade = grade,
            }
        );
        report.Add("HOUSING item=" + (2010 + grade) + " span=" + span + " endpoint=ordinary-belt");
    }

    private void ValidateGalleryHousings()
    {
        FluidInserterVisuals.Draw(factory, GameCamera.main);
        foreach (var sample in galleryHousingSorters)
        {
            var data = factory.entityPool[sample.Entity];
            var arm = factory.factorySystem.inserterPool[data.inserterId];
            var destination = factory.entityPool[sample.Belt].pos;
            var socket = FluidInserterVisuals.ResolveSocket(
                factory,
                sample.Belt,
                -1,
                destination,
                factory.entityPool[sample.Pipe].pos
            );
            var housing = NativeInserterHousing.Get(sample.Grade, false);
            if (
                !FluidInserterVisuals.UsesPipe(factory, sample.Entity)
                || !PipeRuntime.IsPipeObject(factory, sample.Pipe)
                || PipeRuntime.IsPipeObject(factory, sample.Belt)
                || arm.grade != sample.Grade
                || arm.pickTarget != sample.Pipe
                || arm.insertTarget != sample.Belt
                || !socket.Belt
                || socket.Machine
                || housing.Mesh.vertexCount == 0
                || data.colliderId <= 0
                || data.powerConId <= 0
            )
            {
                throw new InvalidOperationException(
                    "Mixed-endpoint housing is incomplete: " + sample.Entity
                );
            }
        }
        if (FluidInserterVisuals.Count != gallerySorters.Count + galleryHousingSorters.Count)
        {
            throw new InvalidOperationException(
                "Mixed-endpoint gallery display count is incomplete."
            );
        }
    }

    private IEnumerator CaptureGalleryHousings()
    {
        PrepareGalleryHousingStage();
        for (int grade = 1; grade <= 4; grade++)
        {
            AddGalleryHousingSorter(grade, 3, -35);
        }
        SetView(0, -35, 12, -14, -0.5f, 0.8f);
        for (int frame = 0; frame < 24; frame++)
        {
            yield return null;
        }
        ValidateGalleryHousings();
        SetGalleryPower(1);
        SaveImage("ingame-sorters-housings.png");

        SetView(-6, -35, 6.5f, -8, -1, 0.8f);
        for (int frame = 0; frame < 12; frame++)
        {
            yield return null;
        }
        SetGalleryPower(1);
        SaveImage("ingame-sorters-housing-detail-1-2.png");
        SetView(6, -35, 6.5f, -8, 1, 0.8f);
        for (int frame = 0; frame < 12; frame++)
        {
            yield return null;
        }
        SetGalleryPower(1);
        SaveImage("ingame-sorters-housing-detail-3-4.png");

        for (int grade = 1; grade <= 4; grade++)
        {
            AddGalleryHousingSorter(grade, 1, -43);
        }
        SetView(0, -39, 26, -15, -1, 0.8f);
        orthographicCapture = true;
        captureOrthographicSize = 8.5f;
        for (int frame = 0; frame < 24; frame++)
        {
            yield return null;
        }
        ValidateGalleryHousings();
        SetGalleryPower(1);
        SaveImage("ingame-sorters-housing-lengths.png");
        report.Add(
            "PASS four native grade housings connect real fluid pipes to ordinary belts at one- and three-unit spans."
        );
    }
}
