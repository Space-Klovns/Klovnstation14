using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Cultivation.Components;
using Content.Server._KS14.Cultivation.Systems;
using Content.Shared._KS14.Cultivation;
using Content.Shared._KS14.Cultivation.Components;
using Content.Shared._KS14.Cultivation.Systems;
using Content.Shared.Mind;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._KS14.Cultivation;

/// <summary>
///     A master's cultivation passes to their named heir when they die.
/// </summary>
[TestFixture]
[TestOf(typeof(InheritanceSystem))]
public sealed class InheritanceTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Dirty = true };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: KsTestHeirCultivator
  components:
  - type: Cultivator
";

    [Test]
    public async Task TestHeirInheritsHalf()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var mindSystem = SEntMan.System<SharedMindSystem>();
            var inheritanceSystem = SEntMan.System<InheritanceSystem>();
            var cultivationSystem = SEntMan.System<CultivationSystem>();
            var sharedCultivationSystem = SEntMan.System<SharedCultivationSystem>();

            var masterUid = SEntMan.SpawnEntity("KsTestHeirCultivator", map.GridCoords);
            var heirUid = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var strangerUid = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            foreach (var uid in new[] { masterUid, heirUid, strangerUid })
                mindSystem.TransferTo(mindSystem.CreateMind(null), uid);

            sharedCultivationSystem.TryLearnLaw(masterUid, "KsEarth");
            var master = SEntity<CultivatorComponent>(masterUid);
            cultivationSystem.BreakThrough(master);
            cultivationSystem.BreakThrough(master);

            // Only a disciple (or sworn sibling) can be named.
            inheritanceSystem.RecordDisciple(masterUid, heirUid);
            var ev = new NameHeirActionEvent { Performer = masterUid, Target = strangerUid };
            SEntMan.EventBus.RaiseLocalEvent(masterUid, ev);
            var strangerNamed = inheritanceSystem.HeirOf(masterUid) != null;
            ev = new NameHeirActionEvent { Performer = masterUid, Target = heirUid };
            SEntMan.EventBus.RaiseLocalEvent(masterUid, ev);

            var passed = inheritanceSystem.TryPassOn(masterUid);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(strangerNamed, Is.False, "A stranger was named heir.");
                Assert.That(passed, Is.True, "Nothing passed on.");
                Assert.That(SEntMan.TryGetComponent<CultivatorComponent>(heirUid, out var heir) && heir.Insight > 0f, Is.True,
                    "The heir received no cultivation.");
                Assert.That(SEntMan.HasComponent<InheritanceBlessingComponent>(heirUid), Is.True, "The heir's next breakthrough isn't guaranteed.");
                Assert.That(master.Comp.Realm, Is.EqualTo(CultivationRealm.QiCondensation), "The master kept their cultivation.");
                Assert.That(SEntMan.GetComponent<CultivatorComponent>(heirUid).Laws, Does.Contain(new Robust.Shared.Prototypes.ProtoId<Content.Shared._KS14.Cultivation.Prototypes.CultivationLawPrototype>("KsEarth")),
                    "The heir didn't learn the master's law.");
            }
        });
    }
}
