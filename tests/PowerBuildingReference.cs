using System.Collections;
using UnityEngine;

public sealed partial class SceneCapture
{
    private void CreatePowerBuildingReference()
    {
        PlaceFacility(2201, Point(-10, 0), Quaternion.LookRotation(north, up));
        PlaceFacility(2202, Point(0, 0), Quaternion.LookRotation(north, up));
        PlaceFacility(2212, Point(13, 0), Quaternion.LookRotation(north, up));
        GameMain.mainPlayer.position = Point(-30, -30);
        factory.planet.factoryModel.disableEntitySigns = true;
        target = new RenderTexture(1920, 1080, 24);
        target.Create();
        cameraLocked = true;
        SetView(1, 0, 17, 23, 8, 4);
    }

    private IEnumerator CapturePowerBuildingReference()
    {
        SaveImage("ingame-power-reference-family.png");
        SetView(-10, 0, 5, 8, 4, 2.2f);
        for (int frame = 0; frame < 12; frame++)
        {
            yield return null;
        }
        SaveImage("ingame-power-reference-tesla.png");
        SetView(0, 0, 7, 10, 5, 3);
        for (int frame = 0; frame < 12; frame++)
        {
            yield return null;
        }
        SaveImage("ingame-power-reference-wireless.png");
        SetView(13, 0, 9, 14, 6, 4.5f);
        for (int frame = 0; frame < 12; frame++)
        {
            yield return null;
        }
        SaveImage("ingame-power-reference-satellite.png");
    }
}
