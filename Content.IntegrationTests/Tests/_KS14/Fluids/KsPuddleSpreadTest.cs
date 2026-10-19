using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
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
";

    // Deliberately NOT static: these only exist in the test YAML above, and the YAML linter validates static ids.
    private readonly ProtoId<ReagentPrototype> _thinReagent = "KsTestThinReagent";
    private readonly ProtoId<ReagentPrototype> _thickReagent = "KsTestThickReagent";

    private static readonly ProtoId<ReagentPrototype> KlovnideReagent = "SuspendedKlovnideTrioxide";

    private const int FloorRadius = 7;
    private const float SpillVolume = 20f;

    /// <summary>
    ///     20u of a reagent that spreads at 0.5u covers many tiles. The control, at the puddle's own 50u, stays on one.
    /// </summary>
    [Test]
    public async Task ThinReagentSpreadsFar([Values(true, false)] bool thin)
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;
        var testMap = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var mapSystem = entityManager.System<SharedMapSystem>();
            for (var x = -FloorRadius; x <= FloorRadius; x++)
            {
                for (var y = -FloorRadius; y <= FloorRadius; y++)
                    mapSystem.SetTile(testMap.Grid.Owner, testMap.Grid.Comp, new Vector2i(x, y), testMap.Tile.Tile);
            }

            var spilled = new Solution((thin ? _thinReagent : _thickReagent).Id, FixedPoint2.New(SpillVolume));
            Assert.That(entityManager.System<PuddleSystem>().TrySpillAt(new EntityCoordinates(testMap.Grid.Owner, 0.5f, 0.5f), spilled, out _, sound: false),
                "Puddle failed to spill.");
        });

        // The spreader moves one ring of tiles a second.
        await server.WaitRunTicks(30 * 8);

        await server.WaitAssertion(() =>
        {
            var tiles = new HashSet<Vector2i>();
            var mapSystem = entityManager.System<SharedMapSystem>();
            var enumerator = entityManager.EntityQueryEnumerator<PuddleComponent, TransformComponent>();
            while (enumerator.MoveNext(out _, out _, out var transformComponent))
                tiles.Add(mapSystem.CoordinatesToTile(testMap.Grid.Owner, testMap.Grid.Comp, transformComponent.Coordinates));

            if (thin)
                Assert.That(tiles, Has.Count.GreaterThanOrEqualTo(20), "The thin reagent didn't spread far.");
            else
                Assert.That(tiles, Has.Count.EqualTo(1), "The control spread, so it isn't one.");
        });
    }

    [Test]
    public async Task KlovnideTrioxideSpreadsThin()
    {
        await Pair.Server.WaitAssertion(() =>
            Assert.That(SProtoMan.Index(KlovnideReagent).KsSpreadVolume, Is.Not.Null.And.LessThanOrEqualTo(FixedPoint2.New(1)),
                "Suspended klovnide-trioxide doesn't spread thin."));
    }
}
