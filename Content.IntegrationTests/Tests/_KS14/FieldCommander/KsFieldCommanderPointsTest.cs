#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.Client._KS14.FieldCommander;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.FieldCommander;
using Content.Server.GameTicking;
using Content.Shared._KS14.FactionCollision;
using Content.Shared._KS14.FieldCommander;
using Content.Shared._RMC14.Vendors;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.GameTicking;
using Content.Shared.GameTicking.Components;
using Content.Server.Explosion.EntitySystems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Projectiles;
using Content.Shared.Mobs.Systems;
using Content.Shared.Preferences;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.FieldCommander;

/// <summary>
///     Field commander points: earned per kill and lost per teamkill, kept by the player across bodies, spent at the
///         shop; plus what the rule does to defenders' bodies, its intro, and its phase command.
/// </summary>
[TestFixture]
[TestOf(typeof(KsFieldCommanderRuleSystem))]
public sealed class KsFieldCommanderPointsTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static readonly EntProtoId ScenarioRule = "KsScenarioFieldCommander";
    private static readonly EntProtoId NanotrasenObjective = "KsScenarioObjectiveNanotrasen";
    private static readonly EntProtoId Shop = "KsFieldCommanderShop";
    private static readonly EntProtoId ResupplyFlare = "KsFlareSupplyPodRequisitionResupply";

    // Ids that exist only in the test prototypes below.
    private readonly EntProtoId _commanderRule = "KsTestFieldCommanderPointsRule";
    private readonly EntProtoId _defender = "KsTestFieldCommanderPointsDefender";
    private readonly EntProtoId _phaseRule = "KsTestFieldCommanderPhaseRule";
    private readonly EntProtoId _bomb = "KsTestFieldCommanderPointsBomb";
    private readonly EntProtoId _shell = "KsTestFieldCommanderPointsShell";

    // From the private module, which builds without it leave out; an instance field, so the YAML linter skips it.
    private readonly EntProtoId _modifiedXl8 = "KsWeaponRifleLecterXL8EliteOperative";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestFieldCommanderPointsDefender
  components:
  - type: MobState
  - type: MobThresholds
    thresholds:
      0: Alive
      10: Dead
  - type: Damageable
    damageContainer: Biological
  - type: Injurable
  - type: ScenarioFactionMember
    id: Nanotrasen

- type: entity
  id: KsTestFieldCommanderPointsUnit
  components:
  - type: MobState
  - type: MobThresholds
    thresholds:
      0: Alive
      10: Dead
  - type: Damageable
    damageContainer: Biological
  - type: Injurable

- type: entity
  id: KsTestFieldCommanderPointsBomb
  components:
  - type: Explosive
    explosionType: Default
    totalIntensity: 40
    intensitySlope: 10
    maxIntensity: 20

- type: entity
  parent: KsTestFieldCommanderPointsBomb
  id: KsTestFieldCommanderPointsShell
  components:
  - type: Projectile
    damage:
      types:
        Blunt: 0

- type: entity
  parent: BaseGameRule
  id: KsTestFieldCommanderPointsRule
  components:
  - type: KsFieldCommanderRule
    minSpawnDistance: 6
    maxSpawnDistance: 12
    minDefenderDistance: 2
    minPodDefenderDistance: 2
    minPodObjectiveDistance: 6
    defenderSightRange: 1
    safeZoneRadius: 1
    uplinkPrototype: null
    briefingDelay: 1
    pointsPerCost: 25
    teamkillPoints: -150
    phases:
    - name: ks-fieldcommander-phase-landfall
      start: 3
    - name: ks-fieldcommander-phase-probing
      start: 300
      waveInterval: 1000
      firstWaveDelay: 1000
      budget: 2
    - name: ks-fieldcommander-phase-siege
      start: 600
      waveInterval: 1000
      firstWaveDelay: 1000
      budget: 2
    assets:
    - entity: KsTestFieldCommanderPointsUnit
      cost: 2

- type: entity
  parent: BaseGameRule
  id: KsTestFieldCommanderPhaseRule
  components:
  - type: KsFieldCommanderRule
    uplinkPrototype: null
    briefingAnnouncement: null
    phases:
    - name: ks-fieldcommander-phase-landfall
      start: 0
    - name: ks-fieldcommander-phase-probing
      start: 100
    - name: ks-fieldcommander-phase-pressure
      start: 102
";

    /// <summary>
    ///     Floor around the origin, a defender objective, and the player in a defender's body. The commander starts
    ///         with no phase entered, so nothing is sent until a test asks.
    /// </summary>
    private async Task<(TestMapData Map, EntityUid RuleUid, EntityUid PlayerBody)> SetUpOutpost(EntProtoId? commanderRule = null)
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();
        EntityUid ruleUid = default;
        EntityUid playerBody = default;

        await server.WaitPost(() =>
        {
            KsFieldCommanderTestHelpers.FillFloor(server, testMap, halfExtent: 16);

            server.EntMan.SpawnAtPosition(NanotrasenObjective, new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            playerBody = server.EntMan.SpawnAtPosition(_defender, new EntityCoordinates(testMap.Grid, 2.5f, 0.5f));
            server.PlayerMan.SetAttachedEntity(Pair.Player!, playerBody);

            ruleUid = KsFieldCommanderTestHelpers.StartRules(server, commanderRule ?? _commanderRule).CommanderUid;
        });

        // Lets the rule find its objective and prepare the defenders already there.
        await Pair.RunTicksSync(3);
        return (testMap, ruleUid, playerBody);
    }

    private static (int Section, int Entry) FindPlainPricedEntry(CMAutomatedVendorComponent vendorComponent)
    {
        for (var sectionIndex = 0; sectionIndex < vendorComponent.Sections.Count; sectionIndex++)
        {
            var section = vendorComponent.Sections[sectionIndex];
            if (section.TakeAll != null || section.TakeOne != null || section.Choices != null)
                continue;

            for (var entryIndex = 0; entryIndex < section.Entries.Count; entryIndex++)
            {
                var entry = section.Entries[entryIndex];
                if (entry.Points > 0 && entry.LinkedEntries.Count == 0)
                    return (sectionIndex, entryIndex);
            }
        }

        Assert.Fail("the shop sells nothing for points");
        return default;
    }

    /// <summary>
    ///     Gives the player a fresh body, the way the ticker does when they respawn.
    /// </summary>
    private async Task<EntityUid> Respawn(TestMapData testMap)
    {
        var server = Pair.Server;
        EntityUid newBody = default;

        await server.WaitPost(() =>
        {
            newBody = server.EntMan.SpawnAtPosition(_defender, new EntityCoordinates(testMap.Grid, -2.5f, 0.5f));
            server.PlayerMan.SetAttachedEntity(Pair.Player!, newBody);
            server.EntMan.EventBus.RaiseLocalEvent(newBody,
                new PlayerSpawnCompleteEvent(newBody, Pair.Player!, null, lateJoin: true, silent: true, 0, EntityUid.Invalid, new HumanoidCharacterProfile()),
                broadcast: true);
        });

        await Pair.RunTicksSync(2);
        return newBody;
    }

    private int? GetMirroredPoints(EntityUid bodyUid)
    {
        var extraPoints = Pair.Server.EntMan.GetComponentOrNull<CMVendorUserComponent>(bodyUid)?.ExtraPoints;
        if (extraPoints == null)
            return null;

        return extraPoints.TryGetValue(KsFieldCommanderPoints.PointsType, out var points) ? points : null;
    }

    /// <summary>
    ///     Deals <paramref name="victimUid"/> all it can take, as <paramref name="attackerUid"/>.
    /// </summary>
    private void Kill(EntityUid victimUid, EntityUid attackerUid)
    {
        var damage = new DamageSpecifier { DamageDict = { ["Blunt"] = FixedPoint2.New(10) } };
        Pair.Server.System<DamageableSystem>().ChangeDamage(victimUid, damage, ignoreResistances: true, origin: attackerUid);
        Assert.That(Pair.Server.EntMan.GetComponent<MobStateComponent>(victimUid).CurrentState, Is.EqualTo(MobState.Dead));
    }

    [Test]
    public async Task KillsPayTeamkillsCostAndPointsOutliveTheBody()
    {
        var server = Pair.Server;
        var client = Pair.Client;
        var (testMap, ruleUid, playerBody) = await SetUpOutpost();
        var expected = 0;

        await server.WaitPost(() =>
        {
            var ruleSystem = server.System<KsFieldCommanderRuleSystem>();
            var ruleComponent = server.EntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);

            // A wave of the one asset, so that the unit is the commander's own, with its asset known.
            ruleSystem.SetPhase((ruleUid, ruleComponent, server.EntMan.GetComponent<GameRuleComponent>(ruleUid)), 1);
            ruleSystem.ForceWave((ruleUid, ruleComponent));

            var unitQuery = server.EntMan.AllEntityQueryEnumerator<KsFieldCommanderUnitComponent>();
            Assert.That(unitQuery.MoveNext(out var unitUid, out _), Is.True, "the wave should have sent a unit");

            expected = KsFieldCommanderRuleSystem.GetKillPoints(ruleComponent, ruleComponent.Assets[0]);
            Assert.That(expected, Is.GreaterThan(0));

            Kill(unitUid, playerBody);
            Assert.That(ruleSystem.GetPoints(ruleComponent, Pair.Player!.UserId), Is.EqualTo(expected), "a kill should pay the killer");
            Assert.That(GetMirroredPoints(playerBody), Is.EqualTo(expected), "the shop reads the points off the body");

            // Killing a fellow defender costs more than the kill paid, so the total goes below zero.
            var otherDefender = server.EntMan.SpawnAtPosition(_defender, new EntityCoordinates(testMap.Grid, 4.5f, 0.5f));
            Kill(otherDefender, playerBody);
            expected += ruleComponent.TeamkillPoints;
            Assert.That(expected, Is.LessThan(0));
            Assert.That(ruleSystem.GetPoints(ruleComponent, Pair.Player!.UserId), Is.EqualTo(expected), "a teamkill should cost the killer");
        });

        await Pair.RunTicksSync(3);

        await client.WaitAssertion(() =>
        {
            var lastChange = client.System<KsFieldCommanderPointsSystem>().LastChange;
            Assert.That(lastChange, Is.Not.Null, "the client should have been told its points changed");
            Assert.That(lastChange!.Delta, Is.LessThan(0));
            Assert.That(client.System<KsFieldCommanderPointsSystem>().TryGetLocalPoints(out var shownPoints), Is.True,
                "the client reads its total off its body");
            Assert.That(shownPoints, Is.EqualTo(expected));
        });

        var newBody = await Respawn(testMap);
        await server.WaitAssertion(() =>
        {
            Assert.That(GetMirroredPoints(newBody), Is.EqualTo(expected), "points belong to the player, not the body they died in");
            Assert.That(server.EntMan.HasComponent<KsFactionCollisionShooterComponent>(newBody), Is.True,
                "a defender's shots should pass through their own side");
        });

        await server.WaitAssertion(() =>
            Assert.That(server.EntMan.HasComponent<KsFactionCollisionShooterComponent>(playerBody), Is.True,
                "defenders already in the round when the rule prepared should have been readied too"));
    }

    /// <summary>
    ///     Explosion damage carries no origin, so an explosive kill is credited to whoever set it off: the thrower of
    ///         a timed charge, or the shooter of a shell, whose trigger names what it hit rather than who fired it.
    /// </summary>
    [Test]
    public async Task ExplosiveKillsPayWhoeverSetThemOff([Values] bool fired)
    {
        var server = Pair.Server;
        var (testMap, ruleUid, playerBody) = await SetUpOutpost();
        EntityUid unitUid = default;
        var expected = 0;

        await server.WaitPost(() =>
        {
            var ruleSystem = server.System<KsFieldCommanderRuleSystem>();
            var ruleComponent = server.EntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);
            ruleSystem.SetPhase((ruleUid, ruleComponent, server.EntMan.GetComponent<GameRuleComponent>(ruleUid)), 1);
            ruleSystem.ForceWave((ruleUid, ruleComponent));

            var unitQuery = server.EntMan.AllEntityQueryEnumerator<KsFieldCommanderUnitComponent>();
            Assert.That(unitQuery.MoveNext(out unitUid, out _), Is.True, "the wave should have sent a unit");
            expected = KsFieldCommanderRuleSystem.GetKillPoints(ruleComponent, ruleComponent.Assets[0]);

            var unitCoordinates = server.EntMan.GetComponent<TransformComponent>(unitUid).Coordinates;
            var explosiveUid = server.EntMan.SpawnAtPosition(fired ? _shell : _bomb, unitCoordinates);

            if (fired)
            {
                server.System<SharedProjectileSystem>().SetShooter(explosiveUid, server.EntMan.GetComponent<ProjectileComponent>(explosiveUid), playerBody);

                // What a shell's collide trigger passes as its user: the thing it hit.
                server.System<ExplosionSystem>().TriggerExplosive(explosiveUid, user: unitUid);
            }
            else
            {
                server.System<ExplosionSystem>().TriggerExplosive(explosiveUid, user: playerBody);
            }
        });

        await Pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.GetComponent<MobStateComponent>(unitUid).CurrentState, Is.EqualTo(MobState.Dead),
                "the explosion should have killed the unit");

            var ruleComponent = server.EntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);
            Assert.That(server.System<KsFieldCommanderRuleSystem>().GetPoints(ruleComponent, Pair.Player!.UserId), Is.EqualTo(expected),
                "an explosive kill should pay whoever set the explosive off");
            Assert.That(server.System<ExplosionSystem>().KsProcessingUser, Is.Null, "nothing should be credited outside an explosion");
        });
    }

    [Test]
    public async Task ShopSpendingIsKeptAcrossBodies()
    {
        var server = Pair.Server;
        var (testMap, ruleUid, playerBody) = await SetUpOutpost();
        var price = 0;
        var startingPoints = 0;

        await server.WaitPost(() =>
        {
            var ruleSystem = server.System<KsFieldCommanderRuleSystem>();
            var ruleComponent = server.EntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);

            var shopUid = server.EntMan.SpawnAtPosition(Shop, new EntityCoordinates(testMap.Grid, 2.5f, 1.5f));
            var vendorComponent = server.EntMan.GetComponent<CMAutomatedVendorComponent>(shopUid);
            Assert.That(vendorComponent.PointsType, Is.EqualTo(ruleComponent.PointsType), "the shop should sell for field commander points");

            // Whatever plain priced item the shop sells first, wherever it is listed: no take-one sections or linked
            //      entries, which buy more than the one thing.
            var (sectionIndex, entryIndex) = FindPlainPricedEntry(vendorComponent);
            price = vendorComponent.Sections[sectionIndex].Entries[entryIndex].Points!.Value;

            // Enough for it with some to spare, so what's left shows the price was taken.
            startingPoints = price * 2;
            ruleSystem.SetPoints((ruleUid, ruleComponent), Pair.Player!, startingPoints);

            server.EntMan.EventBus.RaiseLocalEvent(shopUid, new CMVendorVendBuiMsg(sectionIndex, entryIndex, new List<int>())
            {
                Actor = playerBody,
                UiKey = CMAutomatedVendorUI.Key,
            });

            Assert.That(GetMirroredPoints(playerBody), Is.EqualTo(startingPoints - price), "the shop should have taken its price");
            Assert.That(ruleSystem.GetPoints(ruleComponent, Pair.Player!.UserId), Is.EqualTo(startingPoints - price),
                "the rule has to hear of the purchase, or dying would refund it");
        });

        var newBody = await Respawn(testMap);
        await server.WaitAssertion(() =>
            Assert.That(GetMirroredPoints(newBody), Is.EqualTo(startingPoints - price)));
    }

    [Test]
    public async Task IntroGoesOutBeforeTheCommanderSpeaks()
    {
        var server = Pair.Server;
        var (_, ruleUid, _) = await SetUpOutpost();

        await server.WaitAssertion(() =>
        {
            var ruleComponent = server.EntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);
            Assert.That(ruleComponent.BriefingSent, Is.False, "the briefing waits for its delay");
            Assert.That(ruleComponent.PhaseIndex, Is.EqualTo(-1));
        });

        await RunSeconds(1.5f);

        await server.WaitAssertion(() =>
        {
            var ruleComponent = server.EntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);
            Assert.That(ruleComponent.BriefingSent, Is.True);
            Assert.That(ruleComponent.PhaseIndex, Is.EqualTo(-1), "the commander's first phase comes after the briefing");
        });

        await RunSeconds(2f);

        await server.WaitAssertion(() =>
            Assert.That(server.EntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid).PhaseIndex, Is.EqualTo(0)));
    }

    /// <summary>
    ///     The commander only taunts; what the defenders can act on comes from their own intelligence, a moment later.
    /// </summary>
    [Test]
    public async Task WaveIntelFollowsTheTaunt()
    {
        var server = Pair.Server;
        var (_, ruleUid, _) = await SetUpOutpost();
        var intelDelay = 0f;

        await server.WaitPost(() =>
        {
            var ruleSystem = server.System<KsFieldCommanderRuleSystem>();
            var ruleComponent = server.EntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid);
            intelDelay = (float)ruleComponent.IntelDelay.TotalSeconds;

            ruleSystem.SetPhase((ruleUid, ruleComponent, server.EntMan.GetComponent<GameRuleComponent>(ruleUid)), 1);
            ruleSystem.ForceWave((ruleUid, ruleComponent));

            Assert.That(ruleComponent.PendingIntel, Has.Count.EqualTo(1), "the wave's intel should wait to follow the taunt");
        });

        await RunSeconds(intelDelay * 0.5f);
        await server.WaitAssertion(() =>
            Assert.That(server.EntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid).PendingIntel, Has.Count.EqualTo(1),
                "intel shouldn't go out before its delay"));

        await RunSeconds(intelDelay);
        await Pair.RunTicksSync(2);
        await server.WaitAssertion(() =>
            Assert.That(server.EntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid).PendingIntel, Is.Empty,
                "intel should go out once its delay has passed"));
    }

    /// <summary>
    ///     A change can reach the client before the state that carries the new total, or while the player has no body
    ///         with points (a ghost, a fresh body). It must still be shown, not dropped with a readout that has nothing
    ///         to show.
    /// </summary>
    [Test]
    public async Task ChangeIsShownEvenWithoutPointsOnTheBody()
    {
        var client = Pair.Client;

        await client.WaitPost(() =>
            client.EntMan.EventBus.RaiseEvent(EventSource.Network,
                new KsFieldCommanderPointsChangedEvent(50, new Content.Shared._KS14.PopupLocalization.KsPopupPayload("ks-fieldcommander-points-admin", null))));

        // A few frames, which is when the readout used to be thrown away.
        await Pair.RunTicksSync(5);

        await client.WaitAssertion(() =>
        {
            var pointsSystem = client.System<KsFieldCommanderPointsSystem>();
            Assert.That(pointsSystem.TryGetLocalPoints(out _), Is.False, "precondition: the local body has no points");
            Assert.That(pointsSystem.Control?.ChangeCount, Is.EqualTo(1), "the change should still be on screen");
        });
    }

    [Test]
    public async Task PhaseCommandMovesTheTimelineAndItCarriesOn()
    {
        var server = Pair.Server;
        var (_, ruleUid, _) = await SetUpOutpost(_phaseRule);
        var consoleHost = server.ResolveDependency<IConsoleHost>();

        await server.WaitPost(() => consoleHost.ExecuteCommand("ks_fcphase ks-fieldcommander-phase-probing"));
        await Pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
            Assert.That(server.EntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid).PhaseIndex, Is.EqualTo(1),
                "the phase should be found by name, and set"));

        // The next phase starts two seconds after the one jumped to, so the timeline has to carry on from the jump.
        await RunSeconds(3f);

        await server.WaitAssertion(() =>
            Assert.That(server.EntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid).PhaseIndex, Is.EqualTo(2),
                "after a jump, the timeline should run on from the phase jumped to"));

        // Back to the start: the clock must not drag it straight forward again.
        await server.WaitPost(() => consoleHost.ExecuteCommand("ks_fcphase 0"));
        await RunSeconds(1f);

        await server.WaitAssertion(() =>
            Assert.That(server.EntMan.GetComponent<KsFieldCommanderRuleComponent>(ruleUid).PhaseIndex, Is.EqualTo(0),
                "going back a phase should hold"));
    }

    /// <summary>
    ///     The private module adds the NT Paramilitary's rifle to the shop through a partial, which silently does
    ///         nothing if it stops matching. Builds without the module have neither, and skip this.
    /// </summary>
    [Test]
    public async Task ShopSellsTheModuleRifleWhenTheModuleIsThere()
    {
        var server = Pair.Server;

        await server.WaitAssertion(() =>
        {
            if (!server.ProtoMan.HasIndex(_modifiedXl8))
                Assert.Ignore("built without the private module");

            Assert.That(server.ProtoMan.Index(Shop).TryComp<CMAutomatedVendorComponent>(out var vendorComponent, server.EntMan.ComponentFactory), Is.True);

            var entries = new List<EntProtoId>();
            foreach (var section in vendorComponent!.Sections)
            {
                foreach (var entry in section.Entries)
                    entries.Add(entry.Id);
            }

            Assert.That(entries, Does.Contain(_modifiedXl8), "the module's partial should add its rifle to the shop");
            Assert.That(entries, Does.Contain(ResupplyFlare), "and leave the shop's own entries in place");
        });
    }
}
