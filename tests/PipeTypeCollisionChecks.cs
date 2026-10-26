using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using FluidPipes;
using FluidPipes.Integration.Belts;
using HarmonyLib;
using UnityEngine;

public sealed partial class RuntimeChecks
{
    private void PipeProfileCollisionQueries()
    {
        const int buildingMask = 393216;
        const int unrelatedMask = 1 << 19;
        var factory = Fixture(3, 981011, false);
        var tool = new BuildTool_Path
        {
            factory = factory,
            planet = factory.planet,
            handItem = typeCheckItems["shared-variant"],
        };
        var previews = new List<BuildPreview> { new BuildPreview { lrot = Quaternion.identity } };
        AccessTools.Property(typeof(BuildTool), "buildPreviews").SetValue(tool, previews, null);
        Check(
            PipePlanning.TryCollisionGeometry(tool, buildingMask, out _)
                && !PipePlanning.TryCollisionGeometry(tool, unrelatedMask, out _),
            "custom geometry adapts building collision queries and preserves other physics layers"
        );
        Vector3 nativePosition = new Vector3(0, 5000.22f, 0);
        Vector3 typedPosition = new Vector3(0, 5000.6f, 0);
        var objects = new List<GameObject>();
        var overlaps = new Collider[16];
        try
        {
            var sideObstacle = ProfileCollisionObstacle(
                objects,
                typedPosition + Vector3.right * 0.42f,
                17
            );
            var endObstacle = ProfileCollisionObstacle(
                objects,
                typedPosition + Vector3.forward * 0.85f,
                17
            );
            var distantObstacle = ProfileCollisionObstacle(
                objects,
                typedPosition + Vector3.right * 1.8f,
                17
            );
            var ordinaryLayerNear = ProfileCollisionObstacle(
                objects,
                nativePosition + Vector3.right * 0.15f,
                19
            );
            var ordinaryLayerOutside = ProfileCollisionObstacle(
                objects,
                nativePosition + Vector3.right * 0.42f,
                19
            );
            Physics.SyncTransforms();
            Check(
                Physics.OverlapSphereNonAlloc(
                    nativePosition,
                    0.28f,
                    overlaps,
                    buildingMask,
                    QueryTriggerInteraction.Ignore
                ) == 0,
                "native belt-sized collision query misses the wide pipe side and longitudinal obstacles"
            );
            int count = PipeBuildConditions.Sphere(
                nativePosition,
                0.28f,
                overlaps,
                buildingMask,
                QueryTriggerInteraction.Ignore,
                tool
            );
            var contacts = overlaps.Take(count).ToArray();
            Check(
                contacts.Contains(sideObstacle)
                    && contacts.Contains(endObstacle)
                    && !contacts.Contains(distantObstacle),
                "custom node preview checks its full radius and axial length without blocking a distant obstacle"
            );
            count = PipeBuildConditions.Capsule(
                nativePosition + Vector3.left * 0.2f,
                nativePosition + Vector3.right * 0.2f,
                0.28f,
                overlaps,
                buildingMask,
                QueryTriggerInteraction.Ignore,
                tool
            );
            contacts = overlaps.Take(count).ToArray();
            Check(
                contacts.Contains(sideObstacle)
                    && !contacts.Contains(endObstacle)
                    && !contacts.Contains(distantObstacle),
                "segment preview collision follows the shifted profile centerline and thickness"
            );
            count = PipeBuildConditions.Sphere(
                nativePosition,
                0.28f,
                overlaps,
                unrelatedMask,
                QueryTriggerInteraction.Ignore,
                tool
            );
            contacts = overlaps.Take(count).ToArray();
            Check(
                contacts.Contains(ordinaryLayerNear) && !contacts.Contains(ordinaryLayerOutside),
                "nonbuilding collision masks retain the original center and radius"
            );
            foreach (int nativeItemId in new[] { Prototypes.ItemId, 2001 })
            {
                tool.handItem = LDB.items.Select(nativeItemId);
                Check(
                    !PipePlanning.TryCollisionGeometry(tool, buildingMask, out _)
                        && PipeBuildConditions.Sphere(
                            nativePosition,
                            0.28f,
                            overlaps,
                            buildingMask,
                            QueryTriggerInteraction.Ignore,
                            tool
                        ) == 0,
                    "default geometry preserves native overlap behavior for item " + nativeItemId
                );
            }
            tool.handItem = typeCheckItems["shared-variant"];
            var ownCollider = ProfileCollisionObstacle(objects, typedPosition, 17);
            var physics = new PlanetPhysics(factory.planet)
            {
                colChunks = new[] { new ColliderContainer() },
                nearColliderLogic = new NearColliderLogic(),
            };
            factory.planet.physics = physics;
            physics.nearColliderLogic.Init(factory.planet);
            var registrations =
                (Dictionary<int, ColliderObject>)
                    AccessTools
                        .Field(typeof(NearColliderLogic), "colliderObjs")
                        .GetValue(physics.nearColliderLogic);
            RegisterProfileCollider(physics, registrations, 1, ownCollider);
            RegisterProfileCollider(physics, registrations, 2, sideObstacle);
            RegisterProfileCollider(physics, registrations, 3, endObstacle);
            factory.WriteObjectConn(1, 0, true, 2, 1);
            tool.startObjectId = 1;
            Physics.SyncTransforms();
            count = PipeBuildConditions.Sphere(
                nativePosition,
                0.28f,
                overlaps,
                buildingMask,
                QueryTriggerInteraction.Ignore,
                tool
            );
            contacts = overlaps.Take(count).ToArray();
            Check(
                contacts.Contains(endObstacle)
                    && !contacts.Contains(ownCollider)
                    && !contacts.Contains(sideObstacle),
                "native near-collider lookup exempts the selected junction and its neighbor while retaining unrelated pipes"
            );
            physics.colChunks[0].Free();
            registrations.Clear();
            factory.planet.physics = null;
        }
        finally
        {
            foreach (var gameObject in objects)
            {
                Object.DestroyImmediate(gameObject);
            }
            Physics.SyncTransforms();
        }
    }

    private static BoxCollider ProfileCollisionObstacle(
        List<GameObject> objects,
        Vector3 position,
        int layer
    )
    {
        var obstacle = new GameObject("profile-collision-obstacle") { layer = layer };
        objects.Add(obstacle);
        obstacle.transform.position = position;
        var collider = obstacle.AddComponent<BoxCollider>();
        collider.size = Vector3.one * 0.04f;
        return collider;
    }

    private static void RegisterProfileCollider(
        PlanetPhysics physics,
        Dictionary<int, ColliderObject> registrations,
        int entityId,
        Collider collider
    )
    {
        physics.colChunks[0].colliderPool[entityId] = new ColliderData
        {
            idType = entityId,
            pos = collider.transform.position,
            q = Quaternion.identity,
            ext = Vector3.one * 0.02f,
        };
        var registration = (ColliderObject)
            FormatterServices.GetUninitializedObject(typeof(ColliderObject));
        registration.id = entityId;
        registration.live = true;
        registration.collider = collider;
        registrations.Add(entityId, registration);
    }

    private void PipeProfileColliderRefresh()
    {
        var factory = Fixture(2, 981012, false);
        SetTypeCheckPrototype(factory, 1, "structural-frame");
        SetTypeCheckPrototype(factory, 2, "structural-frame");
        factory.WriteObjectConn(1, 0, true, 2, 1);
        var colliders = new[] { new ColliderContainer() };
        factory.planet.physics = new PlanetPhysics(factory.planet) { colChunks = colliders };
        factory.entityPool[1].colliderId = 1;
        factory.entityPool[2].colliderId = 2;
        PipeVisuals.Initialize(factory);
        PipeVisuals.Dirty(factory, 1);
        PipeVisuals.FlushColliderChanges();
        var before = colliders[0].colliderPool[2];
        var replacement = typeCheckItems["structural-upgrade"];
        factory.entityPool[1].protoId = (short)replacement.ID;
        factory.entityPool[1].modelIndex = (short)replacement.ModelIndex;
        PipeLifecyclePatches.Upgraded(factory, 1, replacement);
        PipeVisuals.FlushColliderChanges();
        var after = colliders[0].colliderPool[2];
        try
        {
            Check(
                after.pos.y > before.pos.y + 0.05f && after.ext.y > before.ext.y + 0.05f,
                "upgrading a connected profile refreshes the surviving neighbor collider around its changed endpoint"
            );
        }
        finally
        {
            factory.planet.physics = null;
            colliders[0].Free();
        }
    }
}
