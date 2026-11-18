#nullable enable
using System;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server.Gravity;
using Content.Shared._KS14.Carrying;
using Content.Shared._KS14.ComplexShove;
using Content.Shared.CombatMode;
using Content.Shared.DoAfter;
using Content.Shared.Gravity;
using Content.Shared.Input;
using Content.Shared.Standing;
using Content.Shared.StatusEffectNew;
using Content.Shared.Stunnable;
using Content.Shared.Weapons.Melee;
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

namespace Content.IntegrationTests.Tests._KS14.ComplexShove;

/// <summary>
///     What a shove and a fireman carry do, as the acting player's own client predicts it: the outcome is there the
///         moment the client asks, before the server has said anything, and stays on every tick after. The client
///         predicts well ahead of the server, as it does with real latency, so each of those ticks resets what was
///         predicted and predicts it again.
/// </summary>
[TestFixture]
[TestOf(typeof(ComplexShoveSystem))]
public sealed class KsShovePredictionTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static readonly EntProtoId Human = "MobHuman";
    private static readonly EntProtoId Table = "Table";
    private static readonly EntProtoId Stagger = "KsStatusEffectStaggered";
    private readonly EntProtoId _wall = "KsShovePredictionTestWall";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsShovePredictionTestWall
  components:
  - type: Physics
    bodyType: Static
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeAabb
          bounds: ""-0.5,-0.5,0.5,0.5""
        layer:
        - WallLayer
        mask:
        - FullTileMask
        hard: true
";

    public enum Case
    {
        /// <summary>Nothing behind them: staggered.</summary>
        Open,

        /// <summary>A table behind them: down.</summary>
        IntoTable,

        /// <summary>Someone behind them: both down.</summary>
        IntoPerson,

        /// <summary>A wall behind them, and staggered already: down.</summary>
        StaggeredIntoWall,

        /// <summary>Down already: kicked over and paralyzed.</summary>
        Downed,
    }

    private EntityUid _shoverUid;
    private EntityUid _targetUid;
    private EntityUid _behindUid;

    private async Task SetUp(EntProtoId? behind)
    {
        await Client.WaitPost(() => Client.ResolveDependency<IConfigurationManager>().SetCVar(CVars.NetPredictTickBias, PredictedAheadTicks));

        var testMap = await Pair.CreateTestMap();
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

            _shoverUid = SEntMan.SpawnEntity(Human, new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            _targetUid = SEntMan.SpawnEntity(Human, new EntityCoordinates(testMap.Grid, 1.5f, 0.5f));
            _behindUid = behind == null ? default : SEntMan.SpawnEntity(behind, new EntityCoordinates(testMap.Grid, 2.5f, 0.5f));
            Server.PlayerMan.SetAttachedEntity(Pair.Player!, _shoverUid);
            SEntMan.System<SharedCombatModeSystem>().SetInCombatMode(_shoverUid, true);
        });

        await Pair.RunTicksSync(15);
    }

    private EntityUid ToClient(EntityUid serverUid) => CEntMan.GetEntity(SEntMan.GetNetEntity(serverUid));

    private bool ClientDown(EntityUid clientUid) => CEntMan.System<StandingStateSystem>().IsDown(clientUid);

    [TestCase(Case.Open)]
    [TestCase(Case.IntoTable)]
    [TestCase(Case.IntoPerson)]
    [TestCase(Case.StaggeredIntoWall)]
    [TestCase(Case.Downed)]
    public async Task ClientPredictsTheShove(Case shove)
    {
        await SetUp(shove switch
        {
            Case.IntoTable => Table,
            Case.IntoPerson => Human,
            Case.StaggeredIntoWall => _wall,
            _ => (EntProtoId?)null,
        });

        if (shove == Case.StaggeredIntoWall)
        {
            // The first shove against a wall only staggers; it is the second that the client has to predict.
            await Server.WaitAssertion(() =>
            {
                var meleeSystem = SEntMan.System<SharedMeleeWeaponSystem>();
                Assert.That(meleeSystem.TryGetWeapon(_shoverUid, out var weaponUid, out var weapon));
                meleeSystem.AttemptDisarmAttack(_shoverUid, weaponUid, weapon!, _targetUid);
            });
            await RunSeconds(1.5);
        }
        else if (shove == Case.Downed)
        {
            await Server.WaitPost(() => SEntMan.System<SharedStunSystem>().TryKnockdown(_targetUid, TimeSpan.FromSeconds(30), drop: false));
            await RunSeconds(0.5);
        }

        var clientTargetUid = ToClient(_targetUid);
        var clientBehindUid = shove == Case.IntoPerson ? ToClient(_behindUid) : default;
        var netTarget = SEntMan.GetNetEntity(_targetUid);
        var netCoordinates = SEntMan.GetNetCoordinates(SEntMan.GetComponent<TransformComponent>(_targetUid).Coordinates);

        string? Problem()
        {
            switch (shove)
            {
                case Case.Open:
                    return CEntMan.System<StatusEffectsSystem>().HasStatusEffect(clientTargetUid, Stagger) ? null : "the shoved is not staggered";
                case Case.IntoTable:
                case Case.StaggeredIntoWall:
                    return ClientDown(clientTargetUid) ? null : "the shoved is not down";
                case Case.IntoPerson:
                    if (!ClientDown(clientTargetUid))
                        return "the shoved is not down";
                    return ClientDown(clientBehindUid) ? null : "whoever they were shoved into is not down";
                case Case.Downed:
                    return CEntMan.HasComponent<StunnedComponent>(clientTargetUid) ? null : "the kicked is not paralyzed";
                default:
                    throw new ArgumentOutOfRangeException(nameof(shove));
            }
        }

        await Client.WaitAssertion(() =>
        {
            Assert.That(Problem(), Is.Not.Null, "The outcome must not be there before the shove, or this shows nothing.");

            // What a right-click in combat mode sends. The client runs its own prediction of it at once.
            CEntMan.RaisePredictiveEvent(new DisarmAttackEvent(netTarget, netCoordinates));

            Assert.That(Problem(), Is.Null, "The client must predict the shove's outcome at once.");
        });

        for (var tick = 1; tick <= WatchedTicks; tick++)
        {
            await Pair.RunTicksSync(1);
            await Client.WaitAssertion(() =>
                Assert.That(Problem(), Is.Null, $"The shove's outcome was lost on the client {tick} ticks after it."));
        }
    }

    /// <summary>
    ///     Alt-clicking someone lying down starts lifting them on the client at once, and the client has them on its
    ///         back when its own lift ends, before the server says so.
    /// </summary>
    [Test]
    public async Task ClientPredictsTheCarry()
    {
        await SetUp(null);
        await Server.WaitPost(() => SEntMan.System<SharedStunSystem>().TryKnockdown(_targetUid, TimeSpan.FromSeconds(60), drop: false));
        await RunSeconds(0.5);

        var clientShoverUid = ToClient(_shoverUid);
        var clientTargetUid = ToClient(_targetUid);
        var netCoordinates = SEntMan.GetNetCoordinates(SEntMan.GetComponent<TransformComponent>(_targetUid).Coordinates);
        var carryTicks = 0;

        await Server.WaitPost(() =>
        {
            var timing = Server.ResolveDependency<IGameTiming>();
            carryTicks = (int)Math.Ceiling(SEntMan.GetComponent<KsFiremanCarrierComponent>(_shoverUid).CarryTime.TotalSeconds * timing.TickRate);
        });

        await Client.WaitAssertion(() =>
        {
            // The alt-click, through the client's input system as a click goes.
            var inputManager = Client.ResolveDependency<IInputManager>();
            var timing = Client.ResolveDependency<IGameTiming>();
            var function = ContentKeyFunctions.AltActivateItemInWorld;
            var message = new ClientFullInputCmdMessage(timing.CurTick, timing.TickFraction,
                inputManager.NetworkBindMap.KeyFunctionID(function))
            {
                State = BoundKeyState.Down,
                Coordinates = CEntMan.GetCoordinates(netCoordinates),
                Uid = clientTargetUid,
            };

            CEntMan.System<InputSystem>().HandleInputCommand(Client.ResolveDependency<IPlayerManager>().LocalSession,
                function, message);

            Assert.That(CEntMan.HasComponent<ActiveDoAfterComponent>(clientShoverUid), "The client must predict the lift starting.");
        });

        // The client runs ahead of the server, so its own lift ends first: it must have them on its back while the
        // server is still lifting.
        var clientFirst = false;
        for (var tick = 1; tick <= carryTicks + WatchedTicks; tick++)
        {
            await Pair.RunTicksSync(1);
            var onClient = false;
            var onServer = false;
            await Client.WaitPost(() => onClient = CEntMan.HasComponent<KsCarryingComponent>(clientShoverUid));
            await Server.WaitPost(() => onServer = SEntMan.HasComponent<KsCarryingComponent>(_shoverUid));

            if (onClient && !onServer)
                clientFirst = true;

            if (onServer)
            {
                Assert.That(onClient, $"The client must be carrying once the server is, {tick} ticks after the alt-click.");
                break;
            }
        }

        await Server.WaitAssertion(() => Assert.That(SEntMan.HasComponent<KsCarryingComponent>(_shoverUid), "The server must end up carrying."));
        Assert.That(clientFirst, "The client must have the carried on its back before the server confirms it.");
    }

    private const int WatchedTicks = 20;

    /// <summary>
    ///     How far ahead of the server the client predicts, standing in for latency.
    /// </summary>
    private const int PredictedAheadTicks = 8;

    private async Task RunSeconds(double seconds)
    {
        var timing = Server.ResolveDependency<IGameTiming>();
        await Pair.RunTicksSync((int)Math.Ceiling(seconds * timing.TickRate));
    }
}
