using UnityEngine;

public sealed partial class SceneCapture
{
    private void PrepareShowcase()
    {
        factory.platformSystem.reformCustomColors[0] = new Color(0.22f, 0.25f, 0.28f);
        factory.platformSystem.RefreshColorsTexture();
        factory.PlanetReformAll(1, 16, true);
        GameMain.mainPlayer.position = Point(-140, -140);
        GameMain.mainPlayer.controller.actionBuild.model.Close();
        factory.planet.factoryModel.disableEntitySigns = true;
        factory.planet.factoryModel.drawPowerArc = false;
        factory.planet.factoryModel.drawPowerConn = false;
        PowerSystemRenderer.powerGraphOn = false;
        PowerSystemRenderer.forceLinesOn = false;
        PowerSystemRenderer.forceDisksOn = false;
        PowerSystemRenderer.forceConsumersOn = false;
        target = new RenderTexture(2560, 1440, 24);
        target.antiAliasing = 4;
        target.Create();
        cameraLocked = true;
        report.Add(
            "CAPTURE 2560x1440 with native foundation, game lighting and production pipe/inserter renderers."
        );
    }
}
