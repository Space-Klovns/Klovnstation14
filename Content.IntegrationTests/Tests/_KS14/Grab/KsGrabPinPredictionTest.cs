#nullable enable
using System;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server.Gravity;
using Content.Shared._KS14.Grab;
using Content.Shared.CombatMode;
using Content.Shared.Gravity;
using Content.Shared.Input;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Standing;
using Content.Shared.Weapons.Melee.Events;
using NUnit.Framework;
using Robust.Client.GameObjects;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Grab;

/// <summary>
///     Pinning a held victim with a shove, as the grabber's own client predicts it: the moment the client asks for the
///         shove, before the server has said anything, the victim is down and pinned, and stays so on every tick
///         after. The client predicts well ahead of the server, as it does with real latency.
/// </summary>
[TestFixture]
[TestOf(typeof(KsGrabSystem))]
public sealed class KsGrabPinPredictionTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static readonly EntProtoId Human = "MobHuman";

    /// <param name="tightenOnClient">Whether the grip is tightened by the client's own ctrl-click just before the
    ///     shove, so that the shove comes before the server has confirmed the grip.</param>
    [TestCase(false)]
    [TestCase(true)]
    public async Task ClientPredictsThePin(bool tightenOnClient)
    {
        await Client.WaitPost(() => Client.ResolveDependency<IConfigurationManager>().SetCVar(CVars.NetPredictTickBias, PredictedAheadTicks));

        var testMap = await Pair.CreateTestMap();
        EntityUid grabberUid = default;
        EntityUid victimUid = default;

        await Server.WaitPost(() =>
        {
            var mapSystem = SEntMan.System<SharedMapSystem>();
            for (var x = -1; x < 9; x++)
            {
                for (var y = -1; y < 3; y++)
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

        var pullingSystem = SEntMan.System<PullingSystem>();
        await Server.WaitPost(() => pullingSystem.TogglePull(victimUid, grabberUid));
        await RunSeconds(SEntMan.GetComponent<KsGrabberComponent>(grabberUid).UpgradeCooldown.TotalSeconds);

        var clientGrabberUid = CEntMan.GetEntity(SEntMan.GetNetEntity(grabberUid));
        var clientVictimUid = CEntMan.GetEntity(SEntMan.GetNetEntity(victimUid));
        var netVictim = SEntMan.GetNetEntity(victimUid);
        var netCoordinates = SEntMan.GetNetCoordinates(new EntityCoordinates(testMap.Grid, 1.5f, 0.5f));

        if (tightenOnClient)
        {
            // The ctrl-click, through the client's input system as a click goes.
            await Client.WaitPost(() =>
            {
                var inputManager = Client.ResolveDependency<IInputManager>();
                var timing = Client.ResolveDependency<IGameTiming>();
                var function = ContentKeyFunctions.TryPullObject;
                var message = new ClientFullInputCmdMessage(timing.CurTick, timing.TickFraction,
                    inputManager.NetworkBindMap.KeyFunctionID(function))
                {
                    State = BoundKeyState.Down,
                    Coordinates = CEntMan.GetCoordinates(netCoordinates),
                    Uid = clientVictimUid,
                };

                CEntMan.System<InputSystem>().HandleInputCommand(Client.ResolveDependency<IPlayerManager>().LocalSession,
                    function, message);
            });

            // A few ticks only, fewer than the client is ahead by: the server has not answered yet when the shove
            // follows, and every one of these ticks resets the grip and predicts it again.
            for (var tick = 1; tick <= TightenedTicks; tick++)
            {
                await Pair.RunTicksSync(1);
                await Client.WaitAssertion(() =>
                    Assert.That(ClientHoldsTight(clientGrabberUid),
                        $"The client lost its own tightened grip {tick} ticks after the ctrl-click."));
            }
        }
        else
        {
            await Server.WaitPost(() => pullingSystem.TogglePull(victimUid, grabberUid));
            await RunSeconds(0.5);
        }

        await Client.WaitAssertion(() =>
        {
            Assert.That(ClientHoldsTight(clientGrabberUid), "The client must hold the victim tight before the shove.");

            // What a right-click in combat mode sends. The client runs its own prediction of it at once.
            CEntMan.RaisePredictiveEvent(new DisarmAttackEvent(netVictim, netCoordinates));

            Assert.Multiple(() =>
            {
                Assert.That(CEntMan.GetComponent<KsGrabberComponent>(clientGrabberUid).Pinned, "The client must predict the pin.");
                Assert.That(CEntMan.System<StandingStateSystem>().IsDown(clientVictimUid), "The client must predict the victim going down.");
            });
        });

        for (var tick = 1; tick <= WatchedTicks; tick++)
        {
            await Pair.RunTicksSync(1);
            await Client.WaitAssertion(() =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(CEntMan.GetComponent<KsGrabberComponent>(clientGrabberUid).Pinned,
                        $"The pin was lost on the client {tick} ticks after the shove.");
                    Assert.That(CEntMan.System<StandingStateSystem>().IsDown(clientVictimUid),
                        $"The victim was back on their feet on the client {tick} ticks after the shove.");
                });
            });
        }

        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.GetComponent<KsGrabberComponent>(grabberUid).Pinned, "The server must pin the victim too."));
    }

    /// <summary>
    ///     Whether the client has the grabber's grip at a stage that holds the victim in place: the kind a shove pins.
    /// </summary>
    private bool ClientHoldsTight(EntityUid clientGrabberUid)
    {
        var grabberComponent = CEntMan.GetComponent<KsGrabberComponent>(clientGrabberUid);
        return CProtoMan.Index(grabberComponent.Stages[grabberComponent.Stage]).BlocksVictimMovement;
    }

    private const int WatchedTicks = 20;

    /// <summary>
    ///     Ticks between the client's ctrl-click and its shove.
    /// </summary>
    private const int TightenedTicks = 4;

    /// <summary>
    ///     How far ahead of the server the client predicts, standing in for latency.
    /// </summary>
    private const int PredictedAheadTicks = 8;

    private async Task RunSeconds(double seconds)
    {
        var timing = Server.ResolveDependency<IGameTiming>();
        await Pair.RunTicksSync((int)Math.Ceiling((seconds + 0.5) * timing.TickRate));
    }
}
