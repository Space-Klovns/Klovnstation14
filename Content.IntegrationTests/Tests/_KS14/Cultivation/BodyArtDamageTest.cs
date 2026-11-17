using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Cultivation.Body;
using Content.Shared._KS14.Cultivation.Body;
using Content.Shared.Actions;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._KS14.Cultivation;

/// <summary>
///     Body arts actually hurt people and break things.
/// </summary>
[TestFixture]
[TestOf(typeof(BodyArtSystem))]
public sealed class BodyArtDamageTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Dirty = true };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: KsTestBodyArtist
  components:
  - type: BodyCultivator
    committed: true
";

    /// <summary>
    ///     Use an art the way a player does: through the action the stage granted.
    /// </summary>
    private void PerformGranted(EntityUid userUid, string actionId, EntityUid? target)
    {
        var actionsSystem = SEntMan.System<SharedActionsSystem>();
        // A technique put away in the Techniques menu is equipped first, as a player would.
        if (SEntMan.TryGetComponent<Content.Shared._KS14.Cultivation.Components.CultivationLoadoutComponent>(userUid, out var loadout))
        {
            foreach (var known in loadout.Learned)
            {
                if (!loadout.Equipped.Contains(known) && SEntMan.GetComponent<MetaDataComponent>(known).EntityPrototype?.ID == actionId)
                    actionsSystem.AddActionDirect(userUid, known);
            }
        }

        foreach (var action in actionsSystem.GetActions(userUid))
        {
            if (SEntMan.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID != actionId)
                continue;

            if (target is { } targetUid)
                actionsSystem.SetEventTarget(action, targetUid);

            actionsSystem.PerformAction(userUid, action, predicted: false);
            return;
        }

        Assert.Fail($"{actionId} was never granted.");
    }

    [Test]
    public async Task TestStompHurts()
    {
        var map = await Pair.CreateTestMap();
        EntityUid victimUid = default;
        EntityUid tableUid = default;

        await Server.WaitAssertion(() =>
        {
            var bodySystem = SEntMan.System<SharedBodyCultivationSystem>();
            var userUid = SEntMan.SpawnEntity("KsTestBodyArtist", map.GridCoords);
            victimUid = SEntMan.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(1f, 0f)));
            tableUid = SEntMan.SpawnEntity("Table", map.GridCoords.Offset(new Vector2(0f, 1f)));

            // Stage 2, the first stage that knows the stomp.
            for (var stage = 0; stage < 2; stage++)
                bodySystem.AdvanceStage(userUid);

            PerformGranted(userUid, "ActionBodyEarthStomp", target: null);
        });

        await RunTicksSync(2);

        await Server.WaitAssertion(() =>
        {
            var damageableSystem = SEntMan.System<DamageableSystem>();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(damageableSystem.GetTotalDamage(victimUid), Is.GreaterThan(FixedPoint2.Zero), "The stomp hurt nobody.");
                Assert.That(SEntMan.Deleted(tableUid) || damageableSystem.GetTotalDamage(tableUid) > FixedPoint2.Zero, Is.True,
                    "An early stomp left the furniture untouched.");
            }
        });
    }

    [Test]
    public async Task TestBullChargeGoesThroughEverything()
    {
        var map = await Pair.CreateTestMap();
        EntityUid userUid = default;
        EntityUid tableUid = default;
        EntityUid wallUid = default;

        await Server.WaitAssertion(() =>
        {
            var bodySystem = SEntMan.System<SharedBodyCultivationSystem>();
            userUid = SEntMan.SpawnEntity("KsTestBodyArtist", map.GridCoords);
            tableUid = SEntMan.SpawnEntity("Table", map.GridCoords.Offset(new Vector2(1f, 0f)));
            wallUid = SEntMan.SpawnEntity("WallSolid", map.GridCoords.Offset(new Vector2(3f, 0f)));

            // Stage 4, the first stage that knows the charge.
            for (var stage = 0; stage < 4; stage++)
                bodySystem.AdvanceStage(userUid);

            var ev = new BodyBullChargeActionEvent { Performer = userUid, Target = map.GridCoords.Offset(new Vector2(6f, 0f)) };
            SEntMan.EventBus.RaiseLocalEvent(userUid, ev);
            Assert.That(ev.Handled, Is.True, "The charge didn't go off.");
        });

        await RunTicksSync(5);

        await Server.WaitAssertion(() =>
        {
            var travelled = SEntMan.System<SharedTransformSystem>().GetWorldPosition(userUid).X
                - SEntMan.System<SharedTransformSystem>().ToMapCoordinates(map.GridCoords).Position.X;
            using (Assert.EnterMultipleScope())
            {
                Assert.That(SEntMan.Deleted(tableUid), Is.True, "A table survived the charge.");
                Assert.That(SEntMan.Deleted(wallUid), Is.True, "A wall survived the charge.");
                Assert.That(travelled, Is.GreaterThan(6f), "The charge was stopped short by what stood in its way.");
            }
        });
    }

    [Test]
    public async Task TestSkySplittingPalmBreaksWhatItPasses()
    {
        var map = await Pair.CreateTestMap();
        EntityUid wallUid = default;
        EntityUid windowUid = default;
        EntityUid airlockUid = default;

        await Server.WaitAssertion(() =>
        {
            var bodySystem = SEntMan.System<SharedBodyCultivationSystem>();
            var userUid = SEntMan.SpawnEntity("KsTestBodyArtist", map.GridCoords);
            windowUid = SEntMan.SpawnEntity("Window", map.GridCoords.Offset(new Vector2(2f, 0f)));
            airlockUid = SEntMan.SpawnEntity("Airlock", map.GridCoords.Offset(new Vector2(4f, 0f)));
            wallUid = SEntMan.SpawnEntity("WallSolid", map.GridCoords.Offset(new Vector2(6f, 0f)));

            // Stage 7, Vajra Viscera.
            for (var stage = 0; stage < 7; stage++)
                bodySystem.AdvanceStage(userUid);

            var ev = new BodySkySplittingPalmActionEvent { Performer = userUid, Target = map.GridCoords.Offset(new Vector2(8f, 0f)) };
            SEntMan.EventBus.RaiseLocalEvent(userUid, ev);
            Assert.That(ev.Handled, Is.True, "The palm didn't go off.");
        });

        await RunTicksSync(5);

        await Server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(SEntMan.Deleted(windowUid), Is.True, "A window survived the Sky-Splitting Palm.");
                Assert.That(SEntMan.Deleted(airlockUid), Is.True, "An airlock survived the Sky-Splitting Palm.");
                Assert.That(SEntMan.Deleted(wallUid), Is.True, "A wall survived the Sky-Splitting Palm.");
            }
        });
    }

    [Test]
    public async Task TestShatteringFistBreaksWall()
    {
        var map = await Pair.CreateTestMap();
        EntityUid wallUid = default;

        await Server.WaitAssertion(() =>
        {
            var bodySystem = SEntMan.System<SharedBodyCultivationSystem>();
            var userUid = SEntMan.SpawnEntity("KsTestBodyArtist", map.GridCoords);
            wallUid = SEntMan.SpawnEntity("WallSolid", map.GridCoords.Offset(new Vector2(1f, 0f)));
            // Stage 3, the first stage that knows the fist, long before it can blast trenches.
            for (var stage = 0; stage < 3; stage++)
                bodySystem.AdvanceStage(userUid);

            PerformGranted(userUid, "ActionBodyShatteringFist", target: wallUid);
        });

        await RunTicksSync(5);

        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.Deleted(wallUid) || SEntMan.System<DamageableSystem>().GetTotalDamage(wallUid) > FixedPoint2.Zero, Is.True,
                "The Shattering Fist left the wall untouched."));
    }
}
