#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.CCVar;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared.Teleportation.Components;
using Robust.Server.GameObjects;
using Robust.Shared;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Portal;

/// <summary>
///     View portals: the admin link commands and the PVS that lets the client see
///         the destination.
/// </summary>
[TestFixture]
public sealed class KsPortalTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestPortalMob
  components:
  - type: Physics
    bodyType: Dynamic
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeCircle
          radius: 0.25
        density: 10
        hard: true
        layer:
        - MidImpassable
        mask:
        - Impassable
";

    private const string PortalProto = "KsViewPortal";

    private static readonly Vector2 FarAway = new(60f, 0f);

    private async Task<(EntityUid A, EntityUid B, MapId MapId)> SpawnPortalsAsync(Vector2 firstPosition, Vector2 secondPosition)
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;
        var testMap = await Pair.CreateTestMap();

        EntityUid first = default, second = default;
        await server.WaitPost(() =>
        {
            first = entityManager.SpawnEntity(PortalProto, new MapCoordinates(firstPosition, testMap.MapId));
            second = entityManager.SpawnEntity(PortalProto, new MapCoordinates(secondPosition, testMap.MapId));
        });

        return (first, second, testMap.MapId);
    }

    private async Task RunCommandAsync(string command)
    {
        var consoleHost = Pair.Server.ResolveDependency<IConsoleHost>();
        await Pair.Server.WaitPost(() => consoleHost.ExecuteCommand(command));
    }

    private HashSet<EntityUid> LinksOf(EntityUid uid)
    {
        return Pair.Server.EntMan.TryGetComponent<LinkedEntityComponent>(uid, out var link)
            ? new HashSet<EntityUid>(link.LinkedEntities)
            : new HashSet<EntityUid>();
    }

    [Test]
    public async Task TestLinkCommandIsOneToOneAndRelinkReplaces()
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;

        var (a, b, mapId) = await SpawnPortalsAsync(Vector2.Zero, new Vector2(10f, 0f));
        EntityUid c = default;
        await server.WaitPost(() => c = entityManager.SpawnEntity(PortalProto, new MapCoordinates(new Vector2(20f, 0f), mapId)));

        await RunCommandAsync($"ksportallink {entityManager.GetNetEntity(a)} {entityManager.GetNetEntity(b)}");

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(LinksOf(a), Is.EquivalentTo(new[] { b }));
                Assert.That(LinksOf(b), Is.EquivalentTo(new[] { a }));
            }
        });

        // Relinking A to C must drop B entirely, not leave A linked to two portals.
        await RunCommandAsync($"ksportallink {entityManager.GetNetEntity(a)} {entityManager.GetNetEntity(c)}");

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(LinksOf(a), Is.EquivalentTo(new[] { c }));
                Assert.That(LinksOf(c), Is.EquivalentTo(new[] { a }));
                Assert.That(LinksOf(b), Is.Empty, "B kept a link to A after A was relinked.");
                Assert.That(entityManager.EntityExists(b), "Relinking deleted the orphaned portal.");
            }
        });

        await RunCommandAsync($"ksportalunlink {entityManager.GetNetEntity(a)}");

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(LinksOf(a), Is.Empty);
                Assert.That(LinksOf(c), Is.Empty);
                Assert.That(entityManager.EntityExists(a) && entityManager.EntityExists(c));
            }
        });
    }

    [Test]
    public async Task TestPlayerNearPortalIsSubscribedToLinkedPortal()
    {
        await OverrideCVar(Side.Server, CVars.NetPVS, true);

        var server = Pair.Server;
        var entityManager = server.EntMan;
        var transformSystem = entityManager.System<TransformSystem>();

        var session = Pair.Player;
        Assert.That(session, Is.Not.Null, "this test needs a connected session");

        var (a, b, mapId) = await SpawnPortalsAsync(Vector2.Zero, FarAway);
        await RunCommandAsync($"ksportallink {entityManager.GetNetEntity(a)} {entityManager.GetNetEntity(b)}");

        EntityUid viewer = default;
        await server.WaitPost(() =>
        {
            viewer = entityManager.SpawnEntity(null, new MapCoordinates(new Vector2(2f, 0f), mapId));
            server.PlayerMan.SetAttachedEntity(session!, viewer);
        });

        // Longer than the system's half second interval.
        await Pair.RunTicksSync(40);

        await server.WaitAssertion(() =>
        {
            Assert.That(session!.ViewSubscriptions, Does.Not.Contain(b),
                "The session should be subscribed through a dedicated viewer, not to portal B itself (full range).");

            EntityUid? viewerEntity = null;
            foreach (var subscribed in session.ViewSubscriptions)
            {
                if (entityManager.GetComponent<TransformComponent>(subscribed).ParentUid == b)
                    viewerEntity = subscribed;
            }

            Assert.That(viewerEntity, Is.Not.Null,
                "A player beside portal A was not view-subscribed to a viewer at its linked portal B.");

            var expectedScale = server.CfgMan.GetCVar(KsCCVars.PortalViewPvsScale);
            Assert.That(expectedScale, Is.LessThan(1f));
            Assert.That(entityManager.GetComponent<EyeComponent>(viewerEntity!.Value).PvsScale, Is.EqualTo(expectedScale),
                "The viewer's PVS range was not reduced.");
            Assert.That(session.ViewSubscriptions, Does.Not.Contain(a),
                "Portal A's own surroundings are already sent; subscribing to it is a leak of the wrong direction.");
        });

        // Walk well out of range of A (and nowhere near B either).
        await server.WaitPost(() => transformSystem.SetWorldPosition(viewer, new Vector2(0f, 40f)));
        await Pair.RunTicksSync(40);

        await server.WaitAssertion(() =>
        {
            Assert.That(session!.ViewSubscriptions, Does.Not.Contain(b),
                "The subscription to B outlived the player leaving range of A.");
            Assert.That(session.ViewSubscriptions, Has.None.Matches<EntityUid>(
                    subscribed => entityManager.GetComponent<TransformComponent>(subscribed).ParentUid == b),
                "The viewer subscription outlived the player leaving range of A.");
        });
    }
}
