using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using FluidPipes.API;
using HarmonyLib;
using UnityEngine;

public sealed partial class OrbitalRingChecks
{
    private static bool manualOrbitalSubmission;
    private static long submittedProfileTriangles;
    private static int submittedProfileDrawCalls;

    private static bool ProfileSubmissionAllowed() => manualOrbitalSubmission;

    private static void CountProfileSurface(Mesh mesh, int submesh)
    {
        long triangles = (long)mesh.GetIndexCount(submesh) / 3;
        submittedProfileTriangles += triangles;
        if (triangles > 0)
        {
            submittedProfileDrawCalls++;
        }
    }

    private IEnumerator ProfileOrbitalRendering()
    {
        var draw = AccessTools.Method(
            typeof(PipeApi).Assembly.GetType("FluidPipes.PipeVisuals", true),
            "Draw"
        );
        var drawType = AccessTools.Method(
            typeof(PipeApi).Assembly.GetType("FluidPipes.PipeVisuals", true),
            "DrawType"
        );
        var rebuilds = AccessTools.Property(
            typeof(PipeApi).Assembly.GetType("FluidPipes.PipeVisuals", true),
            "Rebuilds"
        );
        Type braces = orbitalIntegration?.Assembly.GetType(
            "ProjectOrbitalRing.Compatibility.OrbitalRingBraces"
        );
        var drawBraces = braces == null ? null : AccessTools.Method(braces, "Draw");
        var clock = new Stopwatch();
        var samplingPatches = new Harmony("local.fluidpipes.orbitalring.render-sampling");
        samplingPatches.Patch(
            draw,
            prefix: new HarmonyMethod(typeof(OrbitalRingChecks), nameof(ProfileSubmissionAllowed))
        );
        samplingPatches.Patch(
            AccessTools.Method(typeof(CargoTraffic), "Draw"),
            prefix: new HarmonyMethod(typeof(OrbitalRingChecks), nameof(ProfileSubmissionAllowed))
        );
        samplingPatches.Patch(
            AccessTools.Method(draw.DeclaringType, "DrawSurface"),
            postfix: new HarmonyMethod(typeof(OrbitalRingChecks), nameof(CountProfileSurface))
        );
        if (drawBraces != null)
        {
            samplingPatches.Patch(
                drawBraces,
                prefix: new HarmonyMethod(
                    typeof(OrbitalRingChecks),
                    nameof(ProfileSubmissionAllowed)
                )
            );
        }
        var pixel = new Texture2D(1, 1, TextureFormat.RGB24, false);
        bool oldCameraEnabled = GameCamera.main.enabled;
        GameCamera.main.enabled = false;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        if (!NativeOrbitalBelts)
        {
            CheckSingleComponentConstruction();
            CheckOrbitalRepairs();
            var ready = AccessTools.Property(draw.DeclaringType, "StructureMeshesReady");
            if (ready != null)
            {
                float started = Time.realtimeSinceStartup;
                int frames = 0;
                do
                {
                    yield return null;
                    frames++;
                    Require(frames < 2400, "structure prewarm deadline");
                } while (!(bool)ready.GetValue(null));
                Record(
                    "STRUCTURE_PREWARM frames="
                        + frames
                        + " elapsed_ms="
                        + ((Time.realtimeSinceStartup - started) * 1000).ToString("F1")
                );
            }
            if (braces != null)
            {
                Require(
                    (int)AccessTools.Property(braces, "ActivePairs").GetValue(null) == 3,
                    "completed seven-ring fixture has three brace groups"
                );
                Record("PASS complete-ring braces active_pairs=3");
            }
        }
        foreach (string view in new[] { "near", "far", "planet" })
        {
            if (!NativeOrbitalBelts)
            {
                TryCaptureOrbitalView(
                    (camera, target) =>
                    {
                        PositionProfileCamera(camera, view);
                        manualOrbitalSubmission = true;
                        clock.Restart();
                        draw.Invoke(null, new object[] { factory, camera });
                        clock.Stop();
                        manualOrbitalSubmission = false;
                        Record(
                            "FIRST_VIEW view="
                                + view
                                + " submit_ms="
                                + clock.Elapsed.TotalMilliseconds.ToString("F3")
                        );
                    }
                );
                yield return null;
            }
            foreach (
                int itemId in NativeOrbitalBelts ? new[] { 0 } : new[] { -1, 6515, 6517, 6516, 0 }
            )
            {
                var submitTimes = new List<double>();
                var renderTimes = new List<double>();
                var frameTimes = new List<double>();
                int initialRebuilds = 0;
                for (int sample = 0; sample < 30; sample++)
                {
                    int currentSample = sample;
                    if (
                        !TryCaptureOrbitalView(
                            (camera, target) =>
                            {
                                PositionProfileCamera(camera, view);
                                clock.Restart();
                                int drawn = 0;
                                submittedProfileTriangles = 0;
                                submittedProfileDrawCalls = 0;
                                manualOrbitalSubmission = true;
                                if (NativeOrbitalBelts)
                                {
                                    factory.cargoTraffic.Draw(camera);
                                }
                                if (itemId >= 0 && !NativeOrbitalBelts)
                                {
                                    drawn = (int)
                                        (itemId == 0 ? draw : drawType).Invoke(
                                            null,
                                            itemId == 0
                                                ? new object[] { factory, camera }
                                                : new object[] { factory, camera, itemId }
                                        );
                                }
                                manualOrbitalSubmission = false;
                                if (itemId == 0 && drawBraces != null)
                                {
                                    manualOrbitalSubmission = true;
                                    drawBraces.Invoke(null, new object[] { factory, camera });
                                    manualOrbitalSubmission = false;
                                }
                                clock.Stop();
                                double submit = clock.Elapsed.TotalMilliseconds;
                                clock.Restart();
                                camera.Render();
                                RenderTexture previous = RenderTexture.active;
                                RenderTexture.active = target;
                                pixel.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
                                RenderTexture.active = previous;
                                clock.Stop();
                                if (currentSample >= 6)
                                {
                                    submitTimes.Add(submit);
                                    renderTimes.Add(clock.Elapsed.TotalMilliseconds);
                                    frameTimes.Add(Time.unscaledDeltaTime * 1000);
                                }
                                if (currentSample == 5)
                                {
                                    initialRebuilds = (int)rebuilds.GetValue(null);
                                }
                                if (currentSample == 29)
                                {
                                    Record(
                                        "RENDER_PROFILE view="
                                            + view
                                            + " item="
                                            + itemId
                                            + " chunks="
                                            + drawn
                                            + " triangles="
                                            + submittedProfileTriangles
                                            + " draw_calls="
                                            + submittedProfileDrawCalls
                                            + " submit_ms="
                                            + submitTimes.Average().ToString("F3")
                                            + " render_fence_ms="
                                            + renderTimes.Average().ToString("F3")
                                            + " frame_ms="
                                            + frameTimes.Average().ToString("F3")
                                            + " frame_p95_ms="
                                            + frameTimes
                                                .OrderBy(value => value)
                                                .ElementAt((int)(frameTimes.Count * 0.95f))
                                                .ToString("F3")
                                            + " rebuilds="
                                            + ((int)rebuilds.GetValue(null) - initialRebuilds)
                                    );
                                    if (itemId == 0)
                                    {
                                        var shadowCount = AccessTools.Property(
                                            draw.DeclaringType,
                                            "ShadowTriangles"
                                        );
                                        if (shadowCount != null)
                                        {
                                            Record(
                                                "SHADOW_PROFILE view="
                                                    + view
                                                    + " triangles="
                                                    + shadowCount.GetValue(null)
                                            );
                                        }
                                        SaveProfileFrame(target, view);
                                    }
                                }
                            }
                        )
                    )
                    {
                        Destroy(pixel);
                        yield break;
                    }
                    yield return null;
                }
            }
        }
        if (!NativeOrbitalBelts && AccessTools.Field(draw.DeclaringType, "distantChunks") != null)
        {
            CaptureBraceDetail(draw, drawBraces);
            VerifyDistantTopologyInvalidation(draw);
        }
        GameCamera.main.enabled = oldCameraEnabled;
        samplingPatches.UnpatchSelf();
        if (!NativeOrbitalBelts)
        {
            RecordMeshInventory();
        }
        else
        {
            long triangles = 0;
            long vertices = 0;
            foreach (
                var batch in (BeltRenderingBatch[])
                    AccessTools
                        .Field(typeof(CargoTraffic), "beltRenderingBatch")
                        .GetValue(factory.cargoTraffic)
            )
            {
                var mesh = Configs.builtin.beltMesh[batch.batchIndex];
                int instances = batch.nodeCursor / batch.nodeWidth;
                triangles += (long)mesh.GetIndexCount(0) / 3 * instances;
                vertices += (long)mesh.vertexCount * instances;
            }
            Record(
                "NATIVE_MESH_PROFILE submitted_vertices="
                    + vertices
                    + " submitted_triangles="
                    + triangles
            );
        }
        Destroy(pixel);
    }

    private void CheckSingleComponentConstruction()
    {
        Type orientation = orbitalIntegration.Assembly.GetType(
            "ProjectOrbitalRing.Compatibility.OrbitalConstructionOrientation"
        );
        if (orientation == null)
        {
            return;
        }
        foreach (int itemId in new[] { 6515, 6517, 6516 })
        {
            Vector3 anchor = factory.entityPool[displayArcs[itemId][0]].pos;
            Vector3 position = anchor.normalized * (anchor.magnitude + 15);
            int pending = factory.AddPrebuildDataWithComponents(
                new PrebuildData
                {
                    protoId = (short)itemId,
                    modelIndex = (short)LDB.items.Select(itemId).ModelIndex,
                    pos = position,
                    rot = Maths.SphericalRotation(position, 0),
                }
            );
            var prebuild = factory.prebuildPool[pending];
            Vector3 tangent = Vector3.Cross(Vector3.up, position).normalized;
            Require(
                Mathf.Abs(Vector3.Dot(prebuild.rot * Vector3.forward, tangent)) > 0.9999f,
                "single component planned ring tangent " + itemId
            );
            Type planning = typeof(PipeApi).Assembly.GetType("FluidPipes.PipePlanningGizmos", true);
            AccessTools.Method(planning, "Select").Invoke(null, new object[] { LDB.items, itemId });
            var points = new List<ConnGizmoObj>();
            AccessTools
                .Method(planning, "AddPoint")
                .Invoke(
                    null,
                    new object[]
                    {
                        points,
                        new ConnGizmoObj
                        {
                            pos = position,
                            rot = prebuild.rot,
                            size = 1,
                        },
                    }
                );
            Require(
                Mathf.Abs(Vector3.Dot(points[0].rot * Vector3.forward, tangent)) > 0.9999f,
                "single component construction gizmo ring tangent " + itemId
            );
            factory.BuildFinally(GameMain.mainPlayer, pending, false, false);
            int completed = Enumerable
                .Range(1, factory.entityCursor - 1)
                .First(entityId =>
                    factory.entityPool[entityId].id == entityId
                    && factory.entityPool[entityId].protoId == itemId
                    && Vector3.Distance(factory.entityPool[entityId].pos, position) < 0.001f
                );
            Require(
                Mathf.Abs(Vector3.Dot(factory.entityPool[completed].rot * Vector3.forward, tangent))
                    > 0.9999f,
                "single component completed ring tangent " + itemId
            );
            factory.RemoveEntityWithComponents(completed, false);
            Record("PASS single-component construction orientation item=" + itemId);
        }
    }

    private void CaptureBraceDetail(
        System.Reflection.MethodInfo draw,
        System.Reflection.MethodInfo drawBraces
    )
    {
        if (drawBraces == null)
        {
            return;
        }
        TryCaptureOrbitalView(
            (camera, target) =>
            {
                var ring = displayArcs[6516];
                Vector3 anchor = factory.entityPool[ring[235]].pos;
                Vector3 up = anchor.normalized;
                Vector3 east = Vector3.Cross(Vector3.up, up).normalized;
                Vector3 north = Vector3.Cross(up, east).normalized;
                Vector3 focus = anchor + up * 4.5f;
                Vector3 offset = north * -20 + east * 8;
                camera.orthographicSize = 7;
                camera.transform.SetPositionAndRotation(
                    focus + offset,
                    Quaternion.LookRotation(-offset, up)
                );
                manualOrbitalSubmission = true;
                draw.Invoke(null, new object[] { factory, camera });
                drawBraces.Invoke(null, new object[] { factory, camera });
                manualOrbitalSubmission = false;
                camera.Render();
                SaveProfileFrame(target, "braces");
            }
        );
    }

    private object DistantChunkForEntity(Type visuals, int entityId)
    {
        var groups = (IDictionary)AccessTools.Field(visuals, "distantChunks").GetValue(null);
        foreach (DictionaryEntry entry in groups)
        {
            object chunk = AccessTools.Field(entry.Value.GetType(), "Meshes").GetValue(entry.Value);
            var entities =
                (HashSet<int>)AccessTools.Field(chunk.GetType(), "Entities").GetValue(chunk);
            if (entities.Contains(entityId))
            {
                return chunk;
            }
        }
        throw new InvalidOperationException("Structure entity has no distant chunk.");
    }

    private void VerifyDistantTopologyInvalidation(System.Reflection.MethodInfo draw)
    {
        List<int> ring = displayArcs[6515];
        int first = ring[ring.Count * 3 / 4];
        int second = ring[ring.Count * 3 / 4 + 1];
        object original = DistantChunkForEntity(draw.DeclaringType, first);
        Type connections = typeof(PipeApi).Assembly.GetType("FluidPipes.PipeConnections", true);
        Require(
            (bool)
                AccessTools
                    .Method(connections, "Disconnect")
                    .Invoke(null, new object[] { factory, first, second }),
            "distant structure disconnect"
        );
        AccessTools
            .Method(orbitalIntegration, "Schedule")
            .Invoke(null, new object[] { factory, first });
        AccessTools.Method(orbitalIntegration, "Update").Invoke(null, null);
        Type braces = orbitalIntegration.Assembly.GetType(
            "ProjectOrbitalRing.Compatibility.OrbitalRingBraces"
        );
        if (braces != null)
        {
            Require(
                (int)AccessTools.Property(braces, "ActivePairs").GetValue(null) == 2,
                "cut component ring removes its complete brace group"
            );
        }
        TryCaptureOrbitalView(
            (camera, target) =>
            {
                PositionProfileCamera(camera, "planet");
                manualOrbitalSubmission = true;
                try
                {
                    draw.Invoke(null, new object[] { factory, camera });
                }
                finally
                {
                    manualOrbitalSubmission = false;
                }
            }
        );
        object disconnected = DistantChunkForEntity(draw.DeclaringType, first);
        Require(
            !ReferenceEquals(original, disconnected),
            "distant mesh invalidates after disconnect"
        );
        ConnectOrbital(first, second);
        AccessTools
            .Method(orbitalIntegration, "Schedule")
            .Invoke(null, new object[] { factory, first });
        AccessTools.Method(orbitalIntegration, "Update").Invoke(null, null);
        if (braces != null)
        {
            Require(
                (int)AccessTools.Property(braces, "ActivePairs").GetValue(null) == 3,
                "reconnect component ring restores its complete brace group"
            );
        }
        TryCaptureOrbitalView(
            (camera, target) =>
            {
                PositionProfileCamera(camera, "planet");
                manualOrbitalSubmission = true;
                try
                {
                    draw.Invoke(null, new object[] { factory, camera });
                }
                finally
                {
                    manualOrbitalSubmission = false;
                }
            }
        );
        Require(
            !ReferenceEquals(disconnected, DistantChunkForEntity(draw.DeclaringType, first)),
            "distant mesh invalidates after reconnect"
        );
        Require(
            PipeTypes.GetNeighbors(factory, first).Contains(second),
            "distant structure reconnect"
        );
        Record("PASS distant mesh cache invalidation on cut/reconnect");
    }

    private void PositionProfileCamera(Camera camera, string view)
    {
        if (view == "planet")
        {
            Vector3 position =
                new Vector3(-0.3f, 0.55f, -1).normalized * factory.planet.realRadius * 4;
            camera.orthographicSize = factory.planet.realRadius * 1.6f;
            camera.transform.SetPositionAndRotation(
                position,
                Quaternion.LookRotation(-position, Vector3.up)
            );
            return;
        }
        Vector3 anchor = factory.entityPool[displayArcs[6516][displayArcs[6516].Count / 4]].pos;
        Vector3 focus = anchor + anchor.normalized * 1.28f;
        Vector3 localUp = focus.normalized;
        Vector3 localEast = Vector3.Cross(Vector3.up, localUp).normalized;
        Vector3 localNorth = Vector3.Cross(localUp, localEast).normalized;
        bool distant = view == "far";
        Vector3 offset = distant
            ? localUp * 80 - localNorth * 110 - localEast * 70
            : localUp * 8 - localNorth * 18 - localEast * 7;
        camera.orthographicSize = distant ? 75 : 8;
        camera.transform.SetPositionAndRotation(
            focus + offset,
            Quaternion.LookRotation(-offset, localUp)
        );
    }

    private void SaveProfileFrame(RenderTexture target, string view)
    {
        RenderTexture previous = RenderTexture.active;
        var pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
        try
        {
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            pixels.Apply();
            string path = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(output),
                "orbital-profile-" + view + ".png"
            );
            System.IO.File.WriteAllBytes(path, pixels.EncodeToPNG());
            Record("CAPTURE " + path);
        }
        finally
        {
            RenderTexture.active = previous;
            Destroy(pixels);
        }
    }

    private void RecordMeshInventory()
    {
        Type visuals = typeof(PipeApi).Assembly.GetType("FluidPipes.PipeVisuals", true);
        var chunks = (IDictionary)AccessTools.Field(visuals, "chunks").GetValue(null);
        foreach (int itemId in new[] { 6515, 6517, 6516 })
        {
            long vertices = 0;
            long indices = 0;
            int meshes = 0;
            foreach (DictionaryEntry entry in chunks)
            {
                if (
                    (int)AccessTools.Field(entry.Key.GetType(), "ItemId").GetValue(entry.Key)
                    != itemId
                )
                {
                    continue;
                }
                var cachedMeshes = new List<Mesh>();
                foreach (string field in new[] { "Mesh", "CoreMesh" })
                {
                    cachedMeshes.Add(
                        (Mesh)AccessTools.Field(entry.Value.GetType(), field).GetValue(entry.Value)
                    );
                }
                var details = (IDictionary)
                    AccessTools.Field(entry.Value.GetType(), "DetailMeshes")?.GetValue(entry.Value);
                if (details != null)
                {
                    foreach (object detail in details.Values)
                    {
                        cachedMeshes.Add(
                            (Mesh)AccessTools.Field(detail.GetType(), "Wall").GetValue(detail)
                        );
                        cachedMeshes.Add(
                            (Mesh)AccessTools.Field(detail.GetType(), "Core").GetValue(detail)
                        );
                    }
                }
                foreach (var mesh in cachedMeshes)
                {
                    if (mesh == null)
                    {
                        continue;
                    }
                    meshes++;
                    vertices += mesh.vertexCount;
                    for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                    {
                        indices += (long)mesh.GetIndexCount(submesh);
                    }
                }
            }
            Record(
                "MESH_PROFILE item="
                    + itemId
                    + " meshes="
                    + meshes
                    + " vertices="
                    + vertices
                    + " triangles="
                    + indices / 3
            );
        }
    }
}
