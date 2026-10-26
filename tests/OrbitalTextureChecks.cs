using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using FluidPipes.API;
using HarmonyLib;
using UnityEngine;

public sealed partial class OrbitalRingChecks
{
    private void CheckOrbitalTextureCoordinates()
    {
        Type textureLayoutType = orbitalIntegration.Assembly.GetType(
            "ProjectOrbitalRing.Compatibility.OrbitalTextureLayout",
            true
        );
        float coordinatePeriod = (float)
            AccessTools.Field(textureLayoutType, "CoordinatePeriod").GetRawConstantValue();
        float desiredRepeatLength = (float)
            AccessTools.Field(textureLayoutType, "DesiredRepeatLength").GetRawConstantValue();
        Require(
            coordinatePeriod == 512 && desiredRepeatLength == 4,
            "orbital texture coordinates use a four-unit world-length target"
        );

        CheckOrbitalTextureLengths(textureLayoutType, coordinatePeriod, desiredRepeatLength);
        CheckOrbitalTextureBatches(textureLayoutType);
    }

    private void CheckOrbitalTextureLengths(
        Type textureLayoutType,
        float coordinatePeriod,
        float desiredRepeatLength
    )
    {
        MethodInfo fromBand = AccessTools.Method(textureLayoutType, "FromBand");
        MethodInfo distanceToPhase = AccessTools.Method(textureLayoutType, "DistanceToPhase");
        FieldInfo repeatCountField = AccessTools.Field(textureLayoutType, "RepeatCount");
        FieldInfo repeatLengthField = AccessTools.Field(textureLayoutType, "RepeatLength");
        FieldInfo uvScaleField = AccessTools.Field(textureLayoutType, "UvScale");
        var layoutCache = (IDictionary)
            AccessTools.Field(orbitalPositions, "LayoutCache").GetValue(null);

        foreach (float planetRadius in new[] { 80f, 100f, 200f, 500f })
        {
            var planet = new PlanetData
            {
                id = -(int)planetRadius,
                radius = planetRadius,
                scale = 1,
            };
            try
            {
                object orbitalLayout = AccessTools
                    .Method(orbitalPositions, "GetLayout")
                    .Invoke(null, new object[] { planet });
                double angleUnit = (double)
                    AccessTools.Field(orbitalLayout.GetType(), "AngleUnit").GetValue(orbitalLayout);
                bool hasLowerStationRing = (bool)
                    AccessTools
                        .Property(orbitalLayout.GetType(), "HasLowerStationRing")
                        .GetValue(orbitalLayout);

                foreach (int itemId in new[] { 6515, 6517, 6516 })
                {
                    float centerHeight = OrbitalTextureCenterHeight(itemId);
                    for (int band = 1; band <= 2; band++)
                    {
                        object textureLayout = fromBand.Invoke(
                            null,
                            new object[] { planet, itemId, band, centerHeight }
                        );
                        int repeatCount = (int)repeatCountField.GetValue(textureLayout);
                        float repeatLength = (float)repeatLengthField.GetValue(textureLayout);
                        float uvScale = (float)uvScaleField.GetValue(textureLayout);
                        double latitudeSteps = hasLowerStationRing ? (band == 1 ? 8 : 12) : 3;
                        double centerRadius =
                            planetRadius
                            + 0.2
                            + (itemId == 6515 ? 48 : 43) * 1.3333333
                            + centerHeight;
                        double circumference =
                            Math.PI
                            * 2
                            * centerRadius
                            * Math.Cos(latitudeSteps * angleUnit * Math.PI / 180);
                        Require(
                            repeatCount > 0
                                && Math.Abs(repeatLength * repeatCount - circumference) < 0.001
                                && Math.Abs(repeatLength - desiredRepeatLength)
                                    <= desiredRepeatLength / (2 * repeatCount) + 0.000001,
                            "orbital world-length repeat radius="
                                + planetRadius
                                + " item="
                                + itemId
                                + " band="
                                + band
                        );
                        Require(
                            coordinatePeriod * uvScale == repeatCount
                                && Mathf.Abs(
                                    Mathf.Repeat(-coordinatePeriod * 0.5f * uvScale, 1)
                                        - Mathf.Repeat(coordinatePeriod * 0.5f * uvScale, 1)
                                ) < 0.000001f,
                            "orbital longitude seam closes with an integer texture count"
                        );

                        foreach (float worldSpeed in new[] { -7.5f, 0f, 2.25f })
                        {
                            float phaseSpeed = (float)
                                distanceToPhase.Invoke(textureLayout, new object[] { worldSpeed });
                            Require(
                                Mathf.Abs(phaseSpeed * repeatLength - worldSpeed) < 0.00001f,
                                "orbital phase speed preserves signed world distance per second"
                            );
                        }
                    }
                }
            }
            finally
            {
                layoutCache.Remove(planet);
            }
        }
        Record("PASS orbital texture length and phase speed across four planetary radii");
    }

    private void CheckOrbitalTextureBatches(Type textureLayoutType)
    {
        Type materialLibrary = typeof(PipeApi).Assembly.GetType(
            "FluidPipes.PipeMaterialLibrary",
            true
        );
        MethodInfo effects = AccessTools.Method(materialLibrary, "Effects");
        MethodInfo batchGroup = AccessTools.Method(orbitalIntegration, "BatchGroup");
        ConstructorInfo effectContext = AccessTools.Constructor(
            typeof(PipeEffectContext),
            new[] { typeof(PlanetFactory), typeof(int), typeof(int), typeof(PipeSurfaceRole) }
        );
        FieldInfo animationTime = AccessTools.Field(
            typeof(PipeEffectContext),
            "<TimeSeconds>k__BackingField"
        );

        foreach (int itemId in new[] { 6515, 6517, 6516 })
        {
            var circle = displayArcs[itemId];
            var firstAnimatedTransforms = new Dictionary<PipeSurfaceRole, Vector4>();
            var firstDetailTransforms = new Dictionary<PipeSurfaceRole, Vector4>();
            int firstGroup = -1;
            for (int sample = 0; sample < 4; sample++)
            {
                int entityId = circle[sample * circle.Count / 4];
                int group = (int)batchGroup.Invoke(null, new object[] { factory, entityId });
                object textureLayout = AccessTools
                    .Method(textureLayoutType, "FromBand")
                    .Invoke(
                        null,
                        new object[]
                        {
                            factory.planet,
                            itemId,
                            group / 2 % 4,
                            OrbitalTextureCenterHeight(itemId),
                        }
                    );
                float expectedScale = (float)
                    AccessTools.Field(textureLayoutType, "UvScale").GetValue(textureLayout);
                Func<PipeSurfaceRole, float, MaterialPropertyBlock> sampleEffects = (
                    surface,
                    time
                ) =>
                {
                    object context = effectContext.Invoke(
                        new object[] { factory, entityId, group, surface }
                    );
                    animationTime.SetValue(context, time);
                    var properties = new MaterialPropertyBlock();
                    Require(
                        (bool)effects.Invoke(null, new object[] { itemId, context, properties }),
                        "orbital material effect callback succeeds"
                    );
                    return properties;
                };

                foreach (
                    PipeSurfaceRole surface in new[]
                    {
                        PipeSurfaceRole.Wall,
                        PipeSurfaceRole.InnerWall,
                        PipeSurfaceRole.Fittings,
                        PipeSurfaceRole.Core,
                    }
                )
                {
                    MaterialPropertyBlock first = sampleEffects(surface, 0);
                    MaterialPropertyBlock second = sampleEffects(surface, 0.03125f);
                    MaterialPropertyBlock third = sampleEffects(surface, 0.0625f);
                    Vector4 firstTransform = first.GetVector("_MainTex_ST");
                    Vector4 animatedTransform = second.GetVector("_MainTex_ST");
                    Vector4 detailTransform = second.GetVector("_DetailAlbedoMap_ST");
                    Require(
                        Mathf.Abs(firstTransform.y - expectedScale) < 0.000001f
                            && Mathf.Abs(animatedTransform.y - expectedScale) < 0.000001f
                            && Mathf.Abs(third.GetVector("_MainTex_ST").y - expectedScale)
                                < 0.000001f,
                        "orbital surface shares the band's world-length scale "
                            + itemId
                            + " "
                            + surface
                    );
                    if (surface != PipeSurfaceRole.Core)
                    {
                        Require(
                            Mathf.Abs(detailTransform.y - expectedScale) < 0.000001f
                                && detailTransform.z == 0
                                && detailTransform.w == 0
                                && first.GetVector("_DetailAlbedoMap_ST") == detailTransform
                                && third.GetVector("_DetailAlbedoMap_ST") == detailTransform,
                            "orbital wall details retain a static phase during light animation "
                                + itemId
                                + " "
                                + surface
                        );
                    }
                    if (surface == PipeSurfaceRole.Fittings)
                    {
                        Require(
                            firstTransform == animatedTransform
                                && third.GetVector("_MainTex_ST") == animatedTransform
                                && animatedTransform.z == 0
                                && animatedTransform.w == 0,
                            "orbital structural fittings retain a static world-length phase "
                                + itemId
                        );
                    }

                    if (
                        surface == PipeSurfaceRole.Core
                        || surface == PipeSurfaceRole.Wall && itemId != 6516
                    )
                    {
                        float firstAdvance = OrbitalTexturePhaseDelta(
                            firstTransform.w,
                            animatedTransform.w
                        );
                        float secondAdvance = OrbitalTexturePhaseDelta(
                            firstTransform.w,
                            third.GetVector("_MainTex_ST").w
                        );
                        Require(
                            Mathf.Abs(firstAdvance) > 0.000001f
                                && Mathf.Abs(secondAdvance - firstAdvance * 2) < 0.00001f,
                            "orbital light phase advances at a constant rate "
                                + itemId
                                + " "
                                + surface
                        );
                    }

                    if (sample == 0)
                    {
                        firstGroup = group;
                        firstAnimatedTransforms.Add(surface, animatedTransform);
                        firstDetailTransforms.Add(surface, detailTransform);
                    }
                    else
                    {
                        Require(
                            group == firstGroup
                                && animatedTransform == firstAnimatedTransforms[surface]
                                && detailTransform == firstDetailTransforms[surface],
                            "orbital texture phase is independent of spatial batch representative "
                                + itemId
                                + " "
                                + surface
                        );
                    }
                }
            }
        }
        Record(
            "PASS orbital surface transforms share world-length scale across distant native ring nodes"
        );
        Record("PASS orbital wall details remain stationary while light phases advance");
    }

    private static float OrbitalTextureCenterHeight(int itemId)
    {
        Type registry = typeof(PipeApi).Assembly.GetType("FluidPipes.PipeTypeRegistry", true);
        object entry = AccessTools
            .Method(registry, "Get", new[] { typeof(int) })
            .Invoke(null, new object[] { itemId });
        var definition = (PipeTypeDefinition)
            AccessTools.Field(entry.GetType(), "Definition").GetValue(entry);
        return definition.Geometry.CenterHeight;
    }

    private static float OrbitalTexturePhaseDelta(float from, float to)
    {
        return Mathf.DeltaAngle(from * 360, to * 360) / 360;
    }
}
