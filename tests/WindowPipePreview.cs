using System;
using System.Collections;
using System.Collections.Generic;
using FluidPipes.API;
using UnityEngine;

public sealed partial class SceneCapture
{
    private void CreateWindowPipePreview()
    {
        if (!PipeTypes.TryGet("example.copperpipes:copper", out var copper))
        {
            throw new InvalidOperationException("The copper pipe extension is required.");
        }
        var horizontal = new List<Vector3>();
        var inclined = new List<Vector3>();
        var vertical = new List<Vector3>();
        for (int index = 0; index <= 8; index++)
        {
            horizontal.Add(new Vector3(index - 4, 0, 0));
            inclined.Add(new Vector3(index - 4, index * 0.45f, 0));
        }
        for (int index = 0; index <= 5; index++)
        {
            vertical.Add(Vector3.up * index);
        }
        Path(copper.ItemId, -1, -3, horizontal.ToArray(), 1000);
        Path(copper.ItemId, -1, 0, horizontal.ToArray(), 0);
        Path(copper.ItemId, -1, 3, inclined.ToArray(), 1000);
        Path(copper.ItemId, 6, 0, vertical.ToArray(), 1000);
        GameMain.mainPlayer.position = Point(-25, -25);
        GameMain.mainPlayer.controller.actionBuild.model.Close();
        factory.planet.factoryModel.disableEntitySigns = true;
        target = new RenderTexture(1920, 1080, 24);
        target.Create();
        cameraLocked = true;
        SetView(0, 0, 14, 12, 8, 2);
    }

    private IEnumerator CaptureWindowPipePreview()
    {
        SaveImage("ingame-window-pipes-overview.png");
        SetView(-1, -3, 4.5f, 4, 1, 0.5f);
        for (int frame = 0; frame < 12; frame++)
        {
            yield return null;
        }
        SaveImage("ingame-window-pipes-filled.png");
        SetView(-1, 0, 4.5f, 4, 1, 0.5f);
        for (int frame = 0; frame < 12; frame++)
        {
            yield return null;
        }
        SaveImage("ingame-window-pipes-empty.png");
        SetView(-1, 3, 5.5f, 4, 2, 2);
        for (int frame = 0; frame < 12; frame++)
        {
            yield return null;
        }
        SaveImage("ingame-window-pipes-inclined.png");
        SetView(6, 0, 4.5f, 6, 2, 2.7f);
        for (int frame = 0; frame < 12; frame++)
        {
            yield return null;
        }
        SaveImage("ingame-window-pipes-vertical.png");
    }
}
