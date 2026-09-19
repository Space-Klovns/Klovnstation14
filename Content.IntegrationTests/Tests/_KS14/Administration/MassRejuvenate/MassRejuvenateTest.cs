using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Pair;
using Content.Server._KS14.Administration.MassRejuvenate;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._KS14.Administration.MassRejuvenate;

/// <summary>
///     The mapper's mass rejuvenate marker: what it heals, what it leaves alone, and that it cleans
///         itself up afterwards.
/// </summary>
/// <remarks>
///     The marker fires on map init rather than component init. On a loading map, component init runs
///         before the entities around the marker are initialized, which made the marker heal nothing.
/// </remarks>
[TestFixture]
[TestOf(typeof(MassRejuvenateSystem))]
public sealed class MassRejuvenateTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    [TestPrototypes]
    private const string Prototypes = @"
# Injurable is what actually stores the damage; Damageable alone raises the events but records nothing.
- type: entity
  id: KsTestRejuvenateTarget
  name: test rejuvenate target
  components:
  - type: Damageable
  - type: Injurable
    damageContainer: Biological

- type: entity
  parent: MarkerBase
  id: KsTestMassRejuvenateAll
  name: test mass rejuvenate marker
  components:
  - type: MassRejuvenateMarker
    radius: 2
    playerControlledOnly: false

# Only heals entities a player is attached to, of which this test has none.
- type: entity
  parent: KsTestMassRejuvenateAll
  id: KsTestMassRejuvenatePlayers
  components:
  - type: MassRejuvenateMarker
    radius: 2
    playerControlledOnly: true
";

    private const string TargetProto = "KsTestRejuvenateTarget";
    private const string AllMarkerProto = "KsTestMassRejuvenateAll";
    private const string PlayersMarkerProto = "KsTestMassRejuvenatePlayers";

    private static readonly FixedPoint2 DamageDealt = FixedPoint2.New(20);

    /// <summary>
    ///     Damages a target, drops a marker next to it, and reports what the target is left with.
    /// </summary>
    /// <param name="targetOffset">How far, in tiles, the target sits from the marker.</param>
    private async Task<(FixedPoint2 RemainingDamage, bool MarkerSurvived)> RunMarker(
        string markerProto,
        float targetOffset)
    {
        var pair = Pair;
        var server = pair.Server;
        var entityManager = server.EntMan;

        var testMap = await pair.CreateTestMap();

        var targetUid = EntityUid.Invalid;
        var markerUid = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            var damageableSystem = entityManager.System<DamageableSystem>();

            targetUid = entityManager.SpawnAtPosition(TargetProto, testMap.GridCoords.Offset(new Vector2(targetOffset, 0f)));

            var damage = new DamageSpecifier();
            damage.DamageDict.Add("Blunt", DamageDealt);
            damageableSystem.TryChangeDamage(targetUid, damage, ignoreResistances: true);

            Assert.That(damageableSystem.GetAllDamage(targetUid).GetTotal(), Is.EqualTo(DamageDealt),
                "The test target did not take the damage the test meant to heal.");

            markerUid = entityManager.SpawnAtPosition(markerProto, testMap.GridCoords);
        });

        // The marker deletes itself with QueueDel, which lands at the end of the tick.
        await server.WaitRunTicks(1);

        var remainingDamage = FixedPoint2.Zero;
        var markerSurvived = false;

        await server.WaitAssertion(() =>
        {
            remainingDamage = entityManager.System<DamageableSystem>().GetAllDamage(targetUid).GetTotal();
            markerSurvived = entityManager.EntityExists(markerUid);
        });

        return (remainingDamage, markerSurvived);
    }

    [Test]
    public async Task TestMarkerHealsEntitiesInRange()
    {
        var (remainingDamage, markerSurvived) = await RunMarker(AllMarkerProto, targetOffset: 1f);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(remainingDamage, Is.EqualTo(FixedPoint2.Zero),
                "A damaged entity one tile from the marker was not healed.");
            Assert.That(markerSurvived, Is.False, "The marker did not clean itself up after firing.");
        }
    }

    [Test]
    public async Task TestMarkerLeavesEntitiesOutOfRangeAlone()
    {
        var (remainingDamage, _) = await RunMarker(AllMarkerProto, targetOffset: 6f);

        Assert.That(remainingDamage, Is.EqualTo(DamageDealt),
            "A damaged entity well outside the marker's radius was healed anyway.");
    }

    [Test]
    public async Task TestPlayerOnlyMarkerIgnoresUnpilotedEntities()
    {
        var (remainingDamage, markerSurvived) = await RunMarker(PlayersMarkerProto, targetOffset: 1f);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(remainingDamage, Is.EqualTo(DamageDealt),
                "A marker set to players only healed an entity with no player attached.");
            Assert.That(markerSurvived, Is.False,
                "The marker did not clean itself up after finding nothing to heal.");
        }
    }
}
