#nullable enable
using System.Linq;
using System.Numerics;
using Content.Client.Popups;
using Content.IntegrationTests.Fixtures;
using Content.Shared.Damage.Components;
using Content.Shared.Teleportation.Components;
using Content.Shared.Teleportation.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Portal;

/// <summary>
///     A portal that frags what goes through it (<see cref="PortalComponent.FragOnTargetTileOccupied"/>) must show
///         "gets shredded" once per victim, however often the victim goes back through before it is gone.
///
///     The client cannot gib (that is server-only), so a predicted victim stays alive and in play on the client
///         until the server's deletion reaches it, and the same victim can re-enter the portal and be fragged
///         again in that time. Popups for the same entity at the same spot stack as "x2, x3, ...".
///         A godmode victim, which survives the gib, is the headless stand-in for that window.
/// </summary>
[TestFixture]
public sealed class KsPortalTelefragTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private const string TelefragMessage = "portal-component-telefrag";

    private static readonly EntProtoId PortalRed = "PortalRed";
    private static readonly EntProtoId PortalBlue = "PortalBlue";
    private static readonly EntProtoId WallSolid = "WallSolid";
    private static readonly EntProtoId HumanProto = "MobHuman";

    private static readonly Vector2 PortalTile = new(0.5f, 0.5f);
    private static readonly Vector2 TargetTile = new(5.5f, 0.5f);

    private async Task<(EntityUid Mob, EntityUid Grid)> SetUpAsync(bool godmode = true)
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();

        EntityUid mob = default;
        await server.WaitPost(() =>
        {
            var portalCoordinates = new EntityCoordinates(testMap.Grid, PortalTile);
            var targetCoordinates = new EntityCoordinates(testMap.Grid, TargetTile);
            var portalUid = SEntMan.SpawnEntity(PortalRed, portalCoordinates);
            var targetUid = SEntMan.SpawnEntity(PortalBlue, targetCoordinates);
            SEntMan.SpawnEntity(WallSolid, targetCoordinates); // makes the target tile occupied, so the arrival is a frag
            SEntMan.System<LinkedEntitySystem>().TryLink(portalUid, targetUid);

            var portalComponent = SEntMan.GetComponent<PortalComponent>(portalUid);
            portalComponent.FragOnTargetTileOccupied = true;
            SEntMan.Dirty(portalUid, portalComponent);

            mob = SEntMan.SpawnEntity(HumanProto, new EntityCoordinates(testMap.Grid, new Vector2(2.5f, 0.5f)));
            if (godmode)
                SEntMan.EnsureComponent<GodmodeComponent>(mob); // survives the gib, like a victim the client has not yet seen die
        });

        await Pair.RunTicksSync(5);
        return (mob, testMap.Grid);
    }

    /// <summary>
    ///     Walks the mob into the portal again, as a victim that is still in play does.
    /// </summary>
    private async Task EnterPortalAsync(EntityUid mob, EntityUid grid, bool expectSurvivor = true)
    {
        await Pair.Server.WaitPost(() =>
        {
            SEntMan.RemoveComponent<PortalTimeoutComponent>(mob);
            SEntMan.System<TransformSystem>().SetCoordinates(mob, new EntityCoordinates(grid, new Vector2(1.6f, 0.5f)));
            SEntMan.System<SharedPhysicsSystem>().SetLinearVelocity(mob, new Vector2(-4f, 0f));
        });

        for (var tick = 0; tick < 6; tick++)
        {
            await Pair.Server.WaitPost(() =>
            {
                if (!SEntMan.Deleted(mob))
                    SEntMan.System<SharedPhysicsSystem>().SetLinearVelocity(mob, new Vector2(-4f, 0f));
            });
            await Pair.RunTicksSync(1);
        }

        if (!expectSurvivor)
        {
            await Pair.Server.WaitAssertion(() => Assert.That(SEntMan.Deleted(mob), "The victim was not gibbed, so this proves nothing about a deleted anchor."));
            return;
        }

        await Pair.Server.WaitPost(() => SEntMan.System<SharedPhysicsSystem>().SetLinearVelocity(mob, Vector2.Zero));

        await Pair.Server.WaitAssertion(() =>
        {
            var position = SEntMan.GetComponent<TransformComponent>(mob).LocalPosition;
            Assert.That(position.X, Is.GreaterThan(3f), $"The mob did not go through the portal; it is at {position}, so this round proves nothing.");
        });
    }

    private async Task<int> TelefragPopupsReceivedAsync()
    {
        await Pair.RunTicksSync(3); // the popups are network messages
        return CEntMan.System<KsTelefragProbeSystem>().ReceivedPopups.Count(popup => popup.Key == TelefragMessage);
    }

    [Test]
    public async Task TestBystanderSeesTheTelefragPopupOnce()
    {
        // The test's client is attached to nothing, so it is a bystander: the popup reaches it only from the server.
        var (mob, grid) = await SetUpAsync(godmode: false);
        await EnterPortalAsync(mob, grid, expectSurvivor: false);
        await Pair.RunTicksSync(3);

        await Pair.Client.WaitAssertion(() =>
        {
            var labels = CEntMan.System<PopupSystem>().WorldLabels.ToArray();
            Assert.That(labels, Has.Length.EqualTo(1), "A bystander should see the telefrag popup once; the victim was gone before it arrived.");
            Assert.That(labels[0].Repeats, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task TestVictimGoingBackThroughIsShownShreddedOnce()
    {
        var (mob, grid) = await SetUpAsync();

        for (var round = 0; round < 4; round++)
            await EnterPortalAsync(mob, grid);

        Assert.That(await TelefragPopupsReceivedAsync(), Is.EqualTo(1),
            "The same victim was fragged again within moments of the first time, and showed the popup again.");
    }

    [Test]
    public async Task TestVictimIsFraggedAgainOnceTheWindowHasPassed()
    {
        var (mob, grid) = await SetUpAsync();

        await EnterPortalAsync(mob, grid);
        Assert.That(await TelefragPopupsReceivedAsync(), Is.EqualTo(1));

        // Well past any window that stands in for the client waiting on the server.
        await Pair.RunSeconds(4);
        await EnterPortalAsync(mob, grid);

        Assert.That(await TelefragPopupsReceivedAsync(), Is.EqualTo(2), "The popup was suppressed for good, not just for a moment.");
    }
}
