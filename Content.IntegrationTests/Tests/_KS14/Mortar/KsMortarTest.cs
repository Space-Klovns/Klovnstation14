#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests._KS14.FieldCommander;
using Content.Shared._KS14.Dissolve;
using Content.Server._KS14.Mortar;
using Content.Server._KS14.NPC.Targeting;
using Content.Shared._KS14.SupplyPod;
using Content.Shared._KS14.SupplyPod.LandingZone;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Mortar;

/// <summary>
///     <see cref="KsMortarComponent"/> and <see cref="KsMortarCrewComponent"/>: a crewman sets its mortar up and fires it; a
///         shell is marked on the ground before it lands and lands on its marker, going for hostiles bunched together, or a
///         priority target over them; an abandoned mortar is anyone's to fire, within its range, cooldown and rounds, and
///         dissolves once nobody uses it, which a crewed one never does.
/// </summary>
[TestFixture]
[TestOf(typeof(KsMortarSystem))]
public sealed class KsMortarTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    // Ids that exist only in the test prototypes below.
    private readonly EntProtoId _crew = "KsTestMortarCrew";
    private readonly EntProtoId _mortar = "KsTestMortar";
    private readonly EntProtoId _target = "KsTestMortarTarget";
    private readonly EntProtoId _priorityTarget = "KsTestMortarPriorityTarget";
    private readonly EntProtoId _impact = "KsTestMortarImpact";
    private readonly EntProtoId _defender = "KsTestMortarDefender";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: SupplyPod
  id: KsTestMortarShell
  components:
  - type: SupplyPod
    height: 10
    fallDuration: 1s
    fallSoundDelay: 0s
  - type: SupplyPodLandingZone
    indicatorProto: KsSupplyPodLandingZone
  - type: TriggerOnSupplyPodLanded
  - type: SpawnOnTrigger
    proto: KsTestMortarImpact
    useMapCoords: true
  - type: DeleteOnTrigger

- type: entity
  id: KsTestMortarImpact

- type: entity
  id: KsTestMortar
  components:
  - type: Transform
    anchored: true
  - type: KsMortar
    shell: KsTestMortarShell
    rounds: 3
    interval: 0.5
    manualCooldown: 1
    minRange: 3
    maxRange: 30
    clusterRadius: 3
    priorityWeight: 5
    spread: 0
    idleTime: 4
    travelTime: 0.5
    incomingTime: 0.5
    emptyTime: 0.5
    abandonedFactions:
    - NanoTrasen
    abandonedPriority: 0.7

- type: entity
  id: KsTestMortarCrew
  components:
  - type: MobState
  - type: NpcFactionMember
    factions:
    - Syndicate
  - type: KsMortarCrew
    mortar: KsTestMortar
    setUpTime: 0.2

- type: entity
  id: KsTestMortarTarget
  components:
  - type: NpcFactionMember
    factions:
    - NanoTrasen

- type: entity
  parent: KsTestMortarTarget
  id: KsTestMortarPriorityTarget
  components:
  - type: KsNpcTargetPriority
    priority: 1

- type: entity
  id: KsTestMortarDefender
  components:
  - type: NpcFactionMember
    factions:
    - NanoTrasen
";

    private static readonly Vector2 Loner = new(10.5f, 0.5f);
    private static readonly Vector2[] Cluster = [new(0.5f, 12.5f), new(1.5f, 12.5f), new(0.5f, 13.5f)];

    private TestMapData _map = default!;

    private async Task SetUpMap()
    {
        _map = await Pair.CreateTestMap();
        await Server.WaitPost(() => KsFieldCommanderTestHelpers.FillFloor(Server, _map, halfExtent: 40));
    }

    private int Ticks(float seconds) => (int) MathF.Ceiling(seconds * (float) Server.ResolveDependency<IGameTiming>().TickRate);

    private EntityUid? FindMortar()
    {
        var query = SEntMan.AllEntityQueryEnumerator<KsMortarComponent>();
        return query.MoveNext(out var uid, out _) ? uid : null;
    }

    [Test]
    public async Task TheCrewSetsUpAndItsShellLandsOnItsMarker()
    {
        var (markerPosition, impactPosition) = await CrewFiresOnce(priorityAtLoner: false);

        Assert.That(Vector2.Distance(impactPosition, markerPosition), Is.LessThan(0.05f), "the shell should land on its marker");
        Assert.That(Vector2.Distance(impactPosition, Cluster[0]) < 0.05f ||
                    Vector2.Distance(impactPosition, Cluster[1]) < 0.05f ||
                    Vector2.Distance(impactPosition, Cluster[2]) < 0.05f, Is.True,
            $"the shell should go for the three bunched together, not the one on its own: it landed at {impactPosition}");
    }

    [Test]
    public async Task APriorityTargetComesBeforeACluster()
    {
        var (_, impactPosition) = await CrewFiresOnce(priorityAtLoner: true);

        Assert.That(Vector2.Distance(impactPosition, Loner), Is.LessThan(0.05f),
            $"a priority target (a turret) should be shelled before three bunched together: it landed at {impactPosition}");
    }

    /// <summary>
    ///     A crewman and its hostiles; waits for the first shell to land, watching for its marker every tick.
    /// </summary>
    private async Task<(Vector2 Marker, Vector2 Impact)> CrewFiresOnce(bool priorityAtLoner)
    {
        await SetUpMap();
        var transformSystem = SEntMan.System<SharedTransformSystem>();

        await Server.WaitPost(() =>
        {
            SEntMan.SpawnEntity(_crew, new EntityCoordinates(_map.Grid.Owner, 0.5f, 0.5f));
            SEntMan.SpawnEntity(priorityAtLoner ? _priorityTarget : _target, new EntityCoordinates(_map.Grid.Owner, Loner));
            foreach (var position in Cluster)
                SEntMan.SpawnEntity(_target, new EntityCoordinates(_map.Grid.Owner, position));
        });

        Vector2? marker = null;
        Vector2? impact = null;
        var markerShownWhileFalling = false;

        for (var tick = 0; tick < Ticks(10f) && impact == null; tick++)
        {
            await Pair.RunTicksSync(1);
            await Server.WaitPost(() =>
            {
                var impactQuery = SEntMan.AllEntityQueryEnumerator<MetaDataComponent>();
                while (impactQuery.MoveNext(out var uid, out var metaDataComponent))
                {
                    if (metaDataComponent.EntityPrototype?.ID != _impact.Id)
                        continue;

                    impact = transformSystem.GetWorldPosition(uid);
                    return;
                }

                var indicatorQuery = SEntMan.AllEntityQueryEnumerator<SupplyPodLandingZoneIndicatorComponent>();
                while (indicatorQuery.MoveNext(out var indicatorUid, out _))
                    marker ??= transformSystem.GetWorldPosition(indicatorUid);

                var podQuery = SEntMan.AllEntityQueryEnumerator<SupplyPodComponent>();
                while (podQuery.MoveNext(out _, out var supplyPodComponent))
                {
                    if (marker != null && !supplyPodComponent.Landed)
                        markerShownWhileFalling = true;
                }
            });
        }

        await Server.WaitAssertion(() => Assert.That(FindMortar(), Is.Not.Null, "the crewman should have set its mortar up"));
        Assert.That(impact, Is.Not.Null, "the crewman should have fired and the shell landed");
        Assert.That(marker, Is.Not.Null, "the shell's landing zone should have been marked before it landed");
        Assert.That(markerShownWhileFalling, Is.True, "the marker should show while the shell is still falling");

        return (marker!.Value, impact!.Value);
    }

    /// <summary>
    ///     Anyone fires an abandoned mortar at a bearing and distance: kept to its range, once per cooldown, while it has
    ///         rounds, and never at what is not a number. It becomes theirs.
    /// </summary>
    [Test]
    public async Task ADefenderFiresAnAbandonedMortar()
    {
        await SetUpMap();
        EntityUid mortarUid = default, defenderUid = default;
        var system = SEntMan.System<KsMortarSystem>();
        var transformSystem = SEntMan.System<SharedTransformSystem>();

        await Server.WaitPost(() =>
        {
            mortarUid = SEntMan.SpawnEntity(_mortar, new EntityCoordinates(_map.Grid.Owner, 0.5f, 0.5f));
            defenderUid = SEntMan.SpawnEntity(_defender, new EntityCoordinates(_map.Grid.Owner, 1.5f, 0.5f));
        });

        await Server.WaitAssertion(() =>
        {
            var mortar = new Entity<KsMortarComponent>(mortarUid, SEntMan.GetComponent<KsMortarComponent>(mortarUid));
            Assert.That(system.TryFireAt(mortar, defenderUid, new Angle(double.NaN), 10f), Is.False, "a bearing that is no number");
            Assert.That(system.TryFireAt(mortar, defenderUid, Angle.FromDegrees(90), float.PositiveInfinity), Is.False, "an endless distance");
            Assert.That(mortar.Comp.Rounds, Is.EqualTo(3), "nothing refused should spend a round");

            Assert.That(system.TryFireAt(mortar, defenderUid, Angle.FromDegrees(90), 10f), Is.True, "east, ten tiles");
            Assert.That(system.TryFireAt(mortar, defenderUid, Angle.FromDegrees(0), 10f), Is.False, "again before its cooldown");
            Assert.That(mortar.Comp.Rounds, Is.EqualTo(2));
            Assert.That(SEntMan.GetComponent<NpcFactionMemberComponent>(mortarUid).Factions,
                Is.EquivalentTo(SEntMan.GetComponent<NpcFactionMemberComponent>(defenderUid).Factions), "whoever fires it is whose it is");
        });

        var (shell, flightTicks) = await WaitForShellTimed();
        Assert.That(Vector2.Distance(shell, new Vector2(10.5f, 0.5f)), Is.LessThan(0.05f), $"the shell should land ten tiles east: {shell}");

        // Travel and incoming, a second in the test mortar: not the shell's own fall time.
        Assert.That(flightTicks, Is.GreaterThanOrEqualTo(Ticks(1f) - 2), "the shell should be in the air for its travel and its incoming");

        await Pair.RunTicksSync(Ticks(1.5f));
        await Server.WaitAssertion(() =>
        {
            var mortar = new Entity<KsMortarComponent>(mortarUid, SEntMan.GetComponent<KsMortarComponent>(mortarUid));
            Assert.That(system.TryFireAt(mortar, defenderUid, Angle.FromDegrees(0), 1000f), Is.True, "past its range is kept to it");
        });

        shell = await WaitForShell();
        Assert.That(Vector2.Distance(shell, new Vector2(0.5f, 30.5f)), Is.LessThan(0.05f), $"the shell should land at its furthest, north: {shell}");

        await Pair.RunTicksSync(Ticks(1.5f));
        await Server.WaitAssertion(() =>
        {
            var mortar = new Entity<KsMortarComponent>(mortarUid, SEntMan.GetComponent<KsMortarComponent>(mortarUid));
            Assert.That(system.TryFireAt(mortar, defenderUid, Angle.FromDegrees(180), 2f), Is.True, "short of its range is kept to it too");
            Assert.That(mortar.Comp.Rounds, Is.Zero);
        });

        await WaitForShell();
        await Pair.RunTicksSync(Ticks(1.5f));
        await Server.WaitAssertion(() =>
        {
            var mortar = new Entity<KsMortarComponent>(mortarUid, SEntMan.GetComponent<KsMortarComponent>(mortarUid));
            Assert.That(system.TryFireAt(mortar, defenderUid, Angle.FromDegrees(180), 10f), Is.False, "with no rounds left");
        });
    }

    /// <summary>
    ///     Where the next shell lands.
    /// </summary>
    private async Task<Vector2> WaitForShell() => (await WaitForShellTimed()).Impact;

    /// <summary>
    ///     Where the next shell lands, and how many ticks that took.
    /// </summary>
    private async Task<(Vector2 Impact, int Ticks)> WaitForShellTimed()
    {
        var transformSystem = SEntMan.System<SharedTransformSystem>();
        Vector2? impact = null;
        var tick = 0;
        for (; tick < Ticks(5f) && impact == null; tick++)
        {
            await Pair.RunTicksSync(1);
            await Server.WaitPost(() =>
            {
                var impactQuery = SEntMan.AllEntityQueryEnumerator<MetaDataComponent>();
                while (impactQuery.MoveNext(out var uid, out var metaDataComponent))
                {
                    if (metaDataComponent.EntityPrototype?.ID != _impact.Id)
                        continue;

                    impact = transformSystem.GetWorldPosition(uid);
                    SEntMan.DeleteEntity(uid);
                    return;
                }
            });
        }

        Assert.That(impact, Is.Not.Null, "the shell should have landed");
        return (impact!.Value, tick);
    }

    /// <summary>
    ///     A crewed mortar never times out and no one else can fire it. The crewman dead, it stays, joins the other side, is a
    ///         priority target, and dissolves once its idle time runs out; using it starts that again.
    /// </summary>
    [Test]
    public async Task AnAbandonedMortarDissolvesUnlessUsed()
    {
        await SetUpMap();
        EntityUid crewUid = default, defenderUid = default, targetUid = default;
        var system = SEntMan.System<KsMortarSystem>();

        await Server.WaitPost(() =>
        {
            crewUid = SEntMan.SpawnEntity(_crew, new EntityCoordinates(_map.Grid.Owner, 0.5f, 0.5f));
            // Something to set up for; taken away once it has, so that it does not fire its rounds away.
            targetUid = SEntMan.SpawnEntity(_target, new EntityCoordinates(_map.Grid.Owner, 10.5f, 0.5f));
            defenderUid = SEntMan.SpawnEntity(_defender, new EntityCoordinates(_map.Grid.Owner, 1.5f, 0.5f));
        });

        EntityUid mortarUid = default;
        for (var tick = 0; tick < Ticks(3f) && mortarUid == default; tick++)
        {
            await Pair.RunTicksSync(1);
            await Server.WaitPost(() => mortarUid = FindMortar() ?? default);
        }

        Assert.That(mortarUid, Is.Not.EqualTo(default(EntityUid)), "the crewman should have set its mortar up");
        await Server.WaitPost(() => SEntMan.DeleteEntity(targetUid));

        // The test mortar idles out in four seconds: three times that, crewed.
        await Pair.RunTicksSync(Ticks(12f));
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.EntityExists(mortarUid) && !SEntMan.HasComponent<KsDissolveComponent>(mortarUid), Is.True,
                "a crewed mortar should never time out");
            var mortar = new Entity<KsMortarComponent>(mortarUid, SEntMan.GetComponent<KsMortarComponent>(mortarUid));
            Assert.That(system.TryFireAt(mortar, defenderUid, Angle.FromDegrees(90), 10f), Is.False, "nobody else fires a crewed mortar");

            SEntMan.System<MobStateSystem>().ChangeMobState(crewUid, MobState.Dead);
        });

        await Pair.RunTicksSync(Ticks(2f));
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.EntityExists(mortarUid), Is.True, "the mortar should stay when its crewman dies");
            Assert.That(SEntMan.TryGetComponent<KsNpcTargetPriorityComponent>(mortarUid, out var priority) && priority.Priority > 0f, Is.True,
                "an abandoned mortar should be a priority target");
            Assert.That(SEntMan.GetComponent<NpcFactionMemberComponent>(mortarUid).Factions,
                Is.EquivalentTo(SEntMan.GetComponent<NpcFactionMemberComponent>(defenderUid).Factions),
                "an abandoned mortar should belong to the side its crew was hostile to");

            // Used two seconds in: its four seconds start again.
            var mortar = new Entity<KsMortarComponent>(mortarUid, SEntMan.GetComponent<KsMortarComponent>(mortarUid));
            Assert.That(system.TryFireAt(mortar, defenderUid, Angle.FromDegrees(90), 10f), Is.True);
        });

        await Pair.RunTicksSync(Ticks(3f));
        await Server.WaitAssertion(() => Assert.That(SEntMan.HasComponent<KsDissolveComponent>(mortarUid), Is.False,
            "a mortar just used should have its idle time started again"));

        await Pair.RunTicksSync(Ticks(2f));
        await Server.WaitAssertion(() => Assert.That(SEntMan.Deleted(mortarUid) || SEntMan.HasComponent<KsDissolveComponent>(mortarUid), Is.True,
            "an abandoned mortar nobody uses should dissolve"));
    }
}
