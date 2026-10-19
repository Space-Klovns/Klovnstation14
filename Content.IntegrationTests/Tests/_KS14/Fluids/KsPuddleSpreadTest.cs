using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Robust.UnitTesting.Pool;
using Content.Server.Fluids.EntitySystems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids;
using Content.Shared.Fluids.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._KS14.Fluids;

/// <summary>
///     A reagent with <see cref="ReagentPrototype.KsSpreadVolume"/> overflows above that volume, not the puddle's own,
///         so a spill of it spreads thin over many tiles.
/// </summary>
[TestFixture]
[TestOf(typeof(SharedPuddleSystem))]
public sealed class KsPuddleSpreadTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: reagent
  id: KsTestThinReagent
  name: reagent-name-water
  desc: reagent-desc-water
  physicalDesc: reagent-physical-desc-translucent
  ksSpreadVolume: 0.5

- type: reagent
  id: KsTestThickReagent
  name: reagent-name-water
  desc: reagent-desc-water
  physicalDesc: reagent-physical-desc-translucent

- type: reagent
  id: KsTestZeroSpreadReagent
  name: reagent-name-water
  desc: reagent-desc-water
  physicalDesc: reagent-physical-desc-translucent
  ksSpreadVolume: 0
";

    // Deliberately NOT static: these only exist in the test YAML above, and the YAML linter validates static ids.
    private readonly ProtoId<ReagentPrototype> _thinReagent = "KsTestThinReagent";
    private readonly ProtoId<ReagentPrototype> _thickReagent = "KsTestThickReagent";
    private readonly ProtoId<ReagentPrototype> _zeroSpreadReagent = "KsTestZeroSpreadReagent";

    private static readonly ProtoId<ReagentPrototype> KlovnideReagent = "SuspendedKlovnideTrioxide";

    private const int TicksPerSecond = 30;

    /// <summary>
    ///     20u of a reagent that spreads at 0.5u covers many tiles. The control, at the puddle's own 50u, stays on one.
    /// </summary>
    [Test]
    public async Task ThinReagentSpreadsFar([Values(true, false)] bool thin)
    {
        var testMap = await Pair.CreateTestMap();
        await LayFloor(testMap, radius: 7);
        await Spill(testMap, new Solution((thin ? _thinReagent : _thickReagent).Id, FixedPoint2.New(20)));

        await Pair.Server.WaitRunTicks(TicksPerSecond * 8);

        var tiles = await CountPuddleTiles(testMap);
        if (thin)
            Assert.That(tiles, Is.GreaterThanOrEqualTo(20), "The thin reagent didn't spread far.");
        else
            Assert.That(tiles, Is.EqualTo(1), "The control spread, so it isn't one.");
    }

    /// <summary>
    ///     A trace of a thin reagent in a puddle of something else barely changes how far the puddle spreads: it pulls
    ///         the overflow volume towards its own by its share of the puddle, no further.
    /// </summary>
    [Test]
    public async Task TraceOfThinReagentDoesNotThinThePuddle()
    {
        var testMap = await Pair.CreateTestMap();
        await LayFloor(testMap, radius: 7);

        var spilled = new Solution();
        spilled.AddReagent(_thickReagent.Id, FixedPoint2.New(40));
        spilled.AddReagent(_thinReagent.Id, FixedPoint2.New(0.4f));
        await Spill(testMap, spilled);

        await Pair.Server.WaitRunTicks(TicksPerSecond * 5);

        Assert.That(await CountPuddleTiles(testMap), Is.EqualTo(1), "40u with a trace of a thin reagent spread.");

        await Pair.Server.WaitAssertion(() =>
        {
            var puddleSystem = Pair.Server.EntMan.System<PuddleSystem>();
            var puddleComponent = new PuddleComponent();

            var half = new Solution();
            half.AddReagent(_thickReagent.Id, FixedPoint2.New(10));
            half.AddReagent(_thinReagent.Id, FixedPoint2.New(10));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(puddleSystem.KsGetOverflowVolume(puddleComponent, new Solution(_thinReagent.Id, FixedPoint2.New(10))),
                    Is.EqualTo(FixedPoint2.New(0.5f)), "A puddle of the thin reagent alone does not spread at its volume.");
                Assert.That(puddleSystem.KsGetOverflowVolume(puddleComponent, half), Is.EqualTo(FixedPoint2.New(25.25f)),
                    "A half and half puddle does not spread halfway between the two.");
            }
        });
    }

    /// <summary>
    ///     A spread volume of zero is held at the smallest a solution stores: the puddle's appearance divides by it.
    /// </summary>
    [Test]
    public async Task ZeroSpreadVolumeIsHeldAboveZero()
    {
        var testMap = await Pair.CreateTestMap();
        await LayFloor(testMap, radius: 2);

        await Pair.Server.WaitAssertion(() =>
            Assert.That(Pair.Server.EntMan.System<PuddleSystem>().KsGetOverflowVolume(new PuddleComponent(), new Solution(_zeroSpreadReagent.Id, FixedPoint2.New(1))),
                Is.EqualTo(SharedPuddleSystem.KsMinimumOverflowVolume)));

        await Spill(testMap, new Solution(_zeroSpreadReagent.Id, FixedPoint2.New(0.2f)));
        await Pair.Server.WaitRunTicks(TicksPerSecond * 3);

        Assert.That(await CountPuddleTiles(testMap), Is.GreaterThan(1), "The reagent did not spread.");
    }

    /// <summary>
    ///     A thin spill in a closed room, with more than it can spread over, ends up level, within rounding, and then leaves its
    ///         puddles' solutions alone rather than splitting off and adding back the overflow every second.
    /// </summary>
    [Test]
    public async Task LevelPuddlesInAClosedRoomAreLeftAlone()
    {
        var testMap = await Pair.CreateTestMap();
        await LayFloor(testMap, radius: 1); // nothing around it but space, which puddles don't spread onto
        await Spill(testMap, new Solution(_thinReagent.Id, FixedPoint2.New(9)));

        await Pair.Server.WaitRunTicks(TicksPerSecond * 15);

        var server = Pair.Server;
        var entityManager = server.EntMan;
        var modifiedTicks = new Dictionary<EntityUid, GameTick>();
        await server.WaitAssertion(() =>
        {
            var enumerator = entityManager.EntityQueryEnumerator<PuddleComponent>();
            while (enumerator.MoveNext(out _, out var puddleComponent))
            {
                Assert.That(puddleComponent.Solution, Is.Not.Null);
                var solutionUid = puddleComponent.Solution.Value.Owner;
                modifiedTicks[solutionUid] = entityManager.GetComponent<MetaDataComponent>(solutionUid).EntityLastModifiedTick;
            }

            Assert.That(modifiedTicks, Has.Count.EqualTo(9), "The spill did not cover the room.");
        });

        await server.WaitRunTicks(TicksPerSecond * 3);

        await server.WaitAssertion(() =>
        {
            foreach (var (solutionUid, modifiedTick) in modifiedTicks)
            {
                Assert.That(entityManager.GetComponent<MetaDataComponent>(solutionUid).EntityLastModifiedTick, Is.EqualTo(modifiedTick),
                    "A level puddle's solution was still being changed.");
            }
        });
    }

    [Test]
    public async Task KlovnideTrioxideSpreadsThin()
    {
        await Pair.Server.WaitAssertion(() =>
            Assert.That(SProtoMan.Index(KlovnideReagent).KsSpreadVolume, Is.Not.Null.And.LessThanOrEqualTo(FixedPoint2.New(1)),
                "Suspended klovnide-trioxide doesn't spread thin."));
    }

    private async Task LayFloor(TestMapData testMap, int radius)
    {
        await Pair.Server.WaitPost(() =>
        {
            var mapSystem = Pair.Server.EntMan.System<SharedMapSystem>();
            for (var x = -radius; x <= radius; x++)
            {
                for (var y = -radius; y <= radius; y++)
                    mapSystem.SetTile(testMap.Grid.Owner, testMap.Grid.Comp, new Vector2i(x, y), testMap.Tile.Tile);
            }
        });
    }

    private async Task Spill(TestMapData testMap, Solution solution)
    {
        await Pair.Server.WaitAssertion(() =>
            Assert.That(Pair.Server.EntMan.System<PuddleSystem>().TrySpillAt(new EntityCoordinates(testMap.Grid.Owner, 0.5f, 0.5f), solution, out _, sound: false),
                "Puddle failed to spill."));
    }

    private async Task<int> CountPuddleTiles(TestMapData testMap)
    {
        var tiles = new HashSet<Vector2i>();
        await Pair.Server.WaitPost(() =>
        {
            var entityManager = Pair.Server.EntMan;
            var mapSystem = entityManager.System<SharedMapSystem>();
            var enumerator = entityManager.EntityQueryEnumerator<PuddleComponent, TransformComponent>();
            while (enumerator.MoveNext(out _, out _, out var transformComponent))
                tiles.Add(mapSystem.CoordinatesToTile(testMap.Grid.Owner, testMap.Grid.Comp, transformComponent.Coordinates));
        });

        return tiles.Count;
    }
}
