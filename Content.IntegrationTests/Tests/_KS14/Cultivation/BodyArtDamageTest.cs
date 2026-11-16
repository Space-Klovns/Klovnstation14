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
