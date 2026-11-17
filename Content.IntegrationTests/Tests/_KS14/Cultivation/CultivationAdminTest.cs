using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Cultivation.Systems;
using Content.Shared._KS14.Cultivation;
using Content.Shared._KS14.Cultivation.Body;
using Content.Shared._KS14.Cultivation.Components;
using Content.Shared._KS14.Cultivation.Systems;
using Content.Shared.Actions;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._KS14.Cultivation;

/// <summary>
///     The admin shortcuts level people the same way play does.
/// </summary>
[TestFixture]
[TestOf(typeof(CultivationAdminSystem))]
public sealed class CultivationAdminTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Dirty = true };

    private bool HasAction(EntityUid uid, string actionId)
    {
        // Known techniques count whether they're on the bar or put away in the Techniques menu.
        return SEntMan.System<SharedActionsSystem>().GetActions(uid)
                   .Any(action => SEntMan.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID == actionId)
               || SEntMan.System<Content.Server._KS14.Cultivation.Systems.CultivationLoadoutSystem>().Knows(uid, actionId);
    }

    [Test]
    public async Task TestQiShortcuts()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var adminSystem = SEntMan.System<CultivationAdminSystem>();
            var sharedCultivationSystem = SEntMan.System<SharedCultivationSystem>();
            var uid = SEntMan.SpawnEntity("MobHuman", map.GridCoords);

            adminSystem.Awaken(uid);
            adminSystem.TeachLaw(uid, "KsFire");
            adminSystem.MaxRealm(uid);
            adminSystem.FillInsight(uid);
            var cultivatorComponent = SComp<CultivatorComponent>(uid);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(cultivatorComponent.Realm, Is.EqualTo(CultivationRealm.NascentSoul));
                Assert.That(sharedCultivationSystem.HasTechnique(cultivatorComponent, "ActionCultivationBodyOutsideBody"), Is.True,
                    "Raising the realm did not open its techniques.");
                Assert.That(sharedCultivationSystem.HasTechnique(cultivatorComponent, "ActionCultivationBurningBlood"), Is.True,
                    "The taught law's techniques did not open.");
                Assert.That(cultivatorComponent.Insight, Is.GreaterThanOrEqualTo(sharedCultivationSystem.GetInsightRequired(cultivatorComponent) ?? 0f),
                    "Filled insight was not enough to try the next step.");
            }
        });
    }

    [Test]
    public async Task TestBodyShortcuts()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var adminSystem = SEntMan.System<CultivationAdminSystem>();
            var mortalUid = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var committedUid = SEntMan.SpawnEntity("MobHuman", map.GridCoords);

            adminSystem.StartBodyPath(mortalUid, commit: false);
            adminSystem.MaxBodyStage(mortalUid);
            adminSystem.StartBodyPath(committedUid, commit: true);
            adminSystem.MaxBodyStage(committedUid);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(SComp<BodyCultivatorComponent>(mortalUid).Stage, Is.EqualTo(BodyCultivatorComponent.MortalStageCap),
                    "A mortal was raised past Copper Skin.");
                Assert.That(SComp<BodyCultivatorComponent>(committedUid).Stage, Is.EqualTo(BodyCultivatorComponent.MaxStage));
                Assert.That(HasAction(committedUid, "ActionBodyPrimordialRoar"), Is.True, "The highest stage's art was not granted.");
            }
        });
    }
}
