#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.Light;
using Content.Shared._KS14.Portal;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Portal;

/// <summary>
///     Experimental light transfer through view portals: the maths that sizes a stand-in, and the client
///         actually placing them at the far doorway.
/// </summary>
[TestFixture]
[TestOf(typeof(KsPortalLightTransfer))]
public sealed class KsPortalLightTransferTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestPortalLamp
  components:
  - type: PointLight
    radius: 6
    energy: 2

- type: entity
  id: KsTestPortalOccluder
  components:
  - type: Occluder
";

    private const string PortalProto = "KsViewPortal";
    private const string LampProto = "KsTestPortalLamp";
    private const string OccluderProto = "KsTestPortalOccluder";

    /// <summary>Well clear of the test map's grid, so nothing on it is in the way.</summary>
    private static readonly Vector2 PortalA = new(100f, 0f);
    private static readonly Vector2 PortalB = new(140f, 0f);

    #region Maths

    [Test]
    public void TransferredLightRunsOutWhereTheSourceWould()
    {
        const float radius = 6f;
        const float distance = 2f;

        Assert.That(KsPortalLightTransfer.TryGetTransferredLight(radius, 1f, 6.8f, 0f, distance, 1, out var transferredRadius, out _));

        // The stand-in hovers like any light, so its edge lands on the floor at sqrt(r'^2 - h^2) from it.
        var reachOnFloor = MathF.Sqrt(transferredRadius * transferredRadius - KsLightAttenuation.LightingHeightSquared);
        Assert.That(reachOnFloor, Is.EqualTo(radius - distance).Within(1e-4f));
    }

    [Test]
    public void TransferredLightArrivesAtTheSourcesBrightnessSharedBetweenSamples()
    {
        const float radius = 6f;
        const float energy = 3f;
        const float falloff = 6.8f;
        const float distance = 2f;
        const int samples = 3;

        Assert.That(KsPortalLightTransfer.TryGetTransferredLight(
            radius, energy, falloff, 0f, distance, samples, out var transferredRadius, out var transferredEnergy));

        var sourceAtDoorway = energy * KsLightAttenuation.Attenuate(
            KsLightAttenuation.GetDistanceOnOwnPlane(distance), radius, falloff, 0f);
        var standInsAtCentre = samples * transferredEnergy * KsLightAttenuation.Attenuate(
            KsLightAttenuation.LightingHeight, transferredRadius, falloff, 0f);

        Assert.That(standInsAtCentre, Is.EqualTo(sourceAtDoorway).Within(1e-4f));
    }

    [Test]
    public void LightThatDoesNotReachThroughIsNotTransferred()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(KsPortalLightTransfer.TryGetTransferredLight(2f, 1f, 6.8f, 0f, 1.9f, 1, out _, out _), Is.False);
            Assert.That(KsPortalLightTransfer.TryGetTransferredLight(2f, 1f, 6.8f, 0f, 3f, 1, out _, out _), Is.False);
        }
    }

    [Test]
    public void MaskRotationTurnsTheMasksTopOntoTheExitDirection()
    {
        using (Assert.EnterMultipleScope())
        {
            foreach (var exit in new[] { Vector2.UnitY, Vector2.UnitX, -Vector2.UnitY, Vector2.Normalize(new Vector2(-1f, 2f)) })
            {
                var rotation = KsPortalLightTransfer.GetMaskRotation(exit);
                var top = rotation.RotateVec(Vector2.UnitY);
                Assert.That(Vector2.Distance(top, exit), Is.LessThan(1e-4f), $"exit {exit}");
            }
        }
    }

    [Test]
    public void SamplesAreSpreadEvenlyAcrossTheDoorway()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(KsPortalLightTransfer.GetSampleOffset(0, 1, 2f), Is.EqualTo(Vector2.Zero));
            Assert.That(KsPortalLightTransfer.GetSampleOffset(0, 2, 2f).X, Is.EqualTo(-0.5f).Within(1e-5f));
            Assert.That(KsPortalLightTransfer.GetSampleOffset(1, 2, 2f).X, Is.EqualTo(0.5f).Within(1e-5f));
        }
    }

    #endregion

    #region Client

    private async Task<(EntityUid A, EntityUid B, MapId MapId)> SpawnLinkedPortalsAsync()
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;
        var testMap = await Pair.CreateTestMap();

        EntityUid a = default, b = default;
        await server.WaitPost(() =>
        {
            a = entityManager.SpawnEntity(PortalProto, new MapCoordinates(PortalA, testMap.MapId));
            b = entityManager.SpawnEntity(PortalProto, new MapCoordinates(PortalB, testMap.MapId));

            // Light is only carried for a viewer near one end, so the player's eye has to be by the portals.
            var viewerUid = entityManager.SpawnEntity(null, new MapCoordinates(PortalB + new Vector2(0f, -4f), testMap.MapId));
            entityManager.EnsureComponent<EyeComponent>(viewerUid);
            server.PlayerMan.SetAttachedEntity(Pair.Player!, viewerUid);
        });

        var consoleHost = server.ResolveDependency<IConsoleHost>();
        await server.WaitPost(() =>
            consoleHost.ExecuteCommand($"ksportallink {entityManager.GetNetEntity(a)} {entityManager.GetNetEntity(b)}"));

        return (a, b, testMap.MapId);
    }

    private async Task<List<(Vector2 Position, Angle Rotation)>> GetClientStandInsAsync()
    {
        var client = Pair.Client;
        var entityManager = client.EntMan;
        var standIns = new List<(Vector2, Angle)>();

        await client.WaitPost(() =>
        {
            var transformSystem = entityManager.System<SharedTransformSystem>();
            var query = entityManager.AllEntityQueryEnumerator<KsPortalLeakedLightComponent, TransformComponent>();
            while (query.MoveNext(out _, out _, out var transformComponent))
            {
                var (position, rotation) = transformSystem.GetWorldPositionRotation(transformComponent);
                standIns.Add((position, rotation));
            }
        });

        return standIns;
    }

    [Test]
    public async Task LightReachingADoorwayShinesOutOfTheLinkedOne()
    {
        var (_, _, mapId) = await SpawnLinkedPortalsAsync();

        // North of A, so it should come out of B heading south.
        await Pair.Server.WaitPost(() =>
            Pair.Server.EntMan.SpawnEntity(LampProto, new MapCoordinates(PortalA + new Vector2(0f, 2f), mapId)));

        await Pair.RunTicksSync(5);
        Assert.That(await GetClientStandInsAsync(), Is.Empty, "Stand-ins were spawned with the experiment switched off.");

        await OverrideCVar(Side.Server, KsCCVars.PortalLightTransfer, true);
        await Pair.RunTicksSync(5);

        var standIns = await GetClientStandInsAsync();
        Assert.That(standIns, Is.Not.Empty, "No light came out of the linked portal.");

        using (Assert.EnterMultipleScope())
        {
            foreach (var (position, rotation) in standIns)
            {
                Assert.That(Vector2.Distance(position, PortalB), Is.LessThanOrEqualTo(0.5f + 1e-3f),
                    "A stand-in was placed off the linked portal's doorway.");

                var exit = rotation.RotateVec(Vector2.UnitY);
                Assert.That(Vector2.Distance(exit, -Vector2.UnitY), Is.LessThan(1e-3f),
                    "A stand-in faces out of the side the light went in on.");
            }
        }

        await OverrideCVar(Side.Server, KsCCVars.PortalLightTransfer, false);
        await Pair.RunTicksSync(5);
        Assert.That(await GetClientStandInsAsync(), Is.Empty, "Switching the experiment off left stand-ins behind.");
    }

    [Test]
    public async Task LightIsNotCarriedForAViewerFarFromBothEnds()
    {
        var (_, _, mapId) = await SpawnLinkedPortalsAsync();

        await Pair.Server.WaitPost(() =>
        {
            var entityManager = Pair.Server.EntMan;
            entityManager.SpawnEntity(LampProto, new MapCoordinates(PortalA + new Vector2(0f, 2f), mapId));

            // Well past either end, on the same map.
            entityManager.System<SharedTransformSystem>().SetWorldPosition(Pair.Player!.AttachedEntity!.Value, PortalA + new Vector2(0f, 500f));
        });

        await OverrideCVar(Side.Server, KsCCVars.PortalLightTransfer, true);
        await Pair.RunTicksSync(5);

        Assert.That(await GetClientStandInsAsync(), Is.Empty, "Light was carried with nobody near enough to see it.");
    }

    [Test]
    public async Task LightBlockedBeforeTheDoorwayIsNotTransferred()
    {
        var (_, _, mapId) = await SpawnLinkedPortalsAsync();

        await Pair.Server.WaitPost(() =>
        {
            var entityManager = Pair.Server.EntMan;
            entityManager.SpawnEntity(LampProto, new MapCoordinates(PortalA + new Vector2(0f, 3f), mapId));
            entityManager.SpawnEntity(OccluderProto, new MapCoordinates(PortalA + new Vector2(0f, 1.5f), mapId));
        });

        await OverrideCVar(Side.Server, KsCCVars.PortalLightTransfer, true);
        await Pair.RunTicksSync(5);

        Assert.That(await GetClientStandInsAsync(), Is.Empty, "Light went through a wall and then through the portal.");
    }

    #endregion
}
