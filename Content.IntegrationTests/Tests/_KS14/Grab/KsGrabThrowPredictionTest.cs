#nullable enable
using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server.Gravity;
using Content.Shared._KS14.Grab;
using Content.Shared._KS14.ThrowMode;
using Content.Shared.CombatMode;
using Content.Shared.Gravity;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory.VirtualItem;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Standing;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Melee;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Grab;

/// <summary>
///     Throwing a grabbed victim, as the thrower's own client predicts it: the moment the client asks for the throw,
///         before the server has said anything, the victim is already where the thrower stands and in flight, and
///         they keep flying on every tick after, without snapping back or waiting for the server.
/// </summary>
[TestFixture]
[TestOf(typeof(KsGrabSystem))]
public sealed class KsGrabThrowPredictionTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static readonly EntProtoId Human = "MobHuman";

    [Test]
    public async Task ClientPredictsTheThrowFromTheThrower()
    {
        var testMap = await Pair.CreateTestMap();
        EntityUid grabberUid = default;
        EntityUid victimUid = default;

        await Server.WaitPost(() =>
        {
            var mapSystem = SEntMan.System<SharedMapSystem>();
            for (var x = -1; x < 9; x++)
            {
                for (var y = -1; y < 9; y++)
                {
                    mapSystem.SetTile(testMap.Grid.Owner, testMap.Grid.Comp, new Vector2i(x, y), testMap.Tile.Tile);
                }
            }

            var gravityComponent = SEntMan.EnsureComponent<GravityComponent>(testMap.Grid.Owner);
            SEntMan.System<GravitySystem>().EnableGravity(testMap.Grid.Owner, gravityComponent);

            grabberUid = SEntMan.SpawnEntity(Human, new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            victimUid = SEntMan.SpawnEntity(Human, new EntityCoordinates(testMap.Grid, 1.5f, 0.5f));
            Server.PlayerMan.SetAttachedEntity(Pair.Player!, grabberUid);
            SEntMan.System<SharedCombatModeSystem>().SetInCombatMode(grabberUid, true);
        });

        await Pair.RunTicksSync(10);

        // Pull, grip aggressively once the delay is over, then shove to pin: only someone down can be thrown.
        var pullingSystem = SEntMan.System<PullingSystem>();
        await Server.WaitPost(() => pullingSystem.TogglePull(victimUid, grabberUid));
        await RunSeconds(SEntMan.GetComponent<KsGrabberComponent>(grabberUid).UpgradeCooldown.TotalSeconds);
        await Server.WaitPost(() => pullingSystem.TogglePull(victimUid, grabberUid));
        await RunSeconds(0.5);
        await Server.WaitAssertion(() =>
        {
            var meleeSystem = SEntMan.System<SharedMeleeWeaponSystem>();
            Assert.That(meleeSystem.TryGetWeapon(grabberUid, out var weaponUid, out var weapon));
            meleeSystem.AttemptDisarmAttack(grabberUid, weaponUid, weapon!, victimUid);
        });
        await RunSeconds(1);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<StandingStateSystem>().IsDown(victimUid), "The victim must be pinned before the throw.");

            var hands = SEntMan.System<SharedHandsSystem>();
            var handItem = hands.EnumerateHeld(grabberUid).Single(held => SEntMan.HasComponent<VirtualItemComponent>(held));
            Assert.That(hands.IsHolding(grabberUid, handItem, out var hand));
            hands.SetActiveHand(grabberUid, hand);

            SEntMan.System<KsThrowModeSystem>().SetEnabled((grabberUid, SEntMan.GetComponent<KsThrowModeComponent>(grabberUid)),
                true, popup: false);
        });

        await Pair.RunTicksSync(10);

        var clientGrabberUid = CEntMan.GetEntity(SEntMan.GetNetEntity(grabberUid));
        var clientVictimUid = CEntMan.GetEntity(SEntMan.GetNetEntity(victimUid));
        var target = SEntMan.GetNetCoordinates(new EntityCoordinates(testMap.Grid, 0.5f, 7.5f));

        await Client.WaitAssertion(() =>
        {
            var transformSystem = CEntMan.System<SharedTransformSystem>();
            var held = (transformSystem.GetWorldPosition(clientVictimUid) - transformSystem.GetWorldPosition(clientGrabberUid)).Length();
            Assert.That(held, Is.GreaterThan(MaxSidewaysDrift), "The victim must be held at arm's length for this to show anything.");

            // What a click in throw mode sends. The client runs its own prediction of it at once.
            CEntMan.RaisePredictiveEvent(new KsThrowModeThrowEvent(target));

            var start = (transformSystem.GetWorldPosition(clientVictimUid) - transformSystem.GetWorldPosition(clientGrabberUid)).Length();
            Assert.Multiple(() =>
            {
                Assert.That(start, Is.LessThan(0.01f), "The client must predict the victim starting from their thrower.");
                Assert.That(CEntMan.HasComponent<ThrownItemComponent>(clientVictimUid), "The client must predict the victim's flight.");
            });
        });

        // The flight itself, tick by tick, on the client alone: thrown north, the victim only ever goes north. Snapping
        // back towards where they were held, or hanging in place until the server catches up, is what an unpredicted
        // throw looks like.
        var lastY = float.MinValue;
        var firstY = 0f;
        for (var tick = 1; tick <= WatchedTicks; tick++)
        {
            await Pair.RunTicksSync(1);
            await Client.WaitAssertion(() =>
            {
                var transformSystem = CEntMan.System<SharedTransformSystem>();
                var position = transformSystem.GetWorldPosition(clientVictimUid);
                var grabberPosition = transformSystem.GetWorldPosition(clientGrabberUid);
                if (tick == 1)
                    firstY = position.Y;

                Assert.Multiple(() =>
                {
                    Assert.That(position.Y, Is.GreaterThanOrEqualTo(lastY - 0.01f),
                        $"The victim went backwards on the client {tick} ticks after the throw.");
                    Assert.That(MathF.Abs(position.X - grabberPosition.X), Is.LessThan(MaxSidewaysDrift),
                        $"The victim left the line of the throw on the client {tick} ticks after it: they must fly from the thrower, not from where they were held.");
                });
                lastY = position.Y;
            });
        }

        Assert.That(lastY - firstY, Is.GreaterThan(1f), "The victim must have flown on the client.");
    }

    /// <summary>
    ///     How many ticks the flight is watched for: well past the round trip to the server.
    /// </summary>
    private const int WatchedTicks = 15;

    /// <summary>
    ///     How far east or west of the thrower a victim thrown north may stray. They were held further off than this,
    ///         so a flight that started from where they were held fails it.
    /// </summary>
    private const float MaxSidewaysDrift = 0.3f;

    private async Task RunSeconds(double seconds)
    {
        var timing = Server.ResolveDependency<IGameTiming>();
        await Pair.RunTicksSync((int)Math.Ceiling((seconds + 0.5) * timing.TickRate));
    }
}
