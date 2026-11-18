using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Cultivation.Components;
using Content.Server._KS14.Cultivation.Systems;
using Content.Shared._KS14.Cultivation;
using Content.Shared._KS14.Cultivation.Body;
using Content.Shared._KS14.Cultivation.Components;
using Content.Shared._KS14.Cultivation.Systems;
using Content.Shared.Actions;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Events;
using Content.Shared.Storage;
using Content.Shared.Weapons.Melee;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._KS14.Cultivation;

/// <summary>
///     Batch F: the legendary artifacts and the secret of the saber and the sword.
/// </summary>
[TestFixture]
[TestOf(typeof(LegendarySystem))]
public sealed class LegendaryTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Dirty = true };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: KsTestLegendaryCultivator
  components:
  - type: Cultivator

- type: entity
  parent: MobHuman
  id: KsTestLegendaryBody
  components:
  - type: BodyCultivator
    committed: true
";

    [Test]
    public async Task TestLegendaryBladeCutsThroughGoldenBody()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var bodySystem = SEntMan.System<SharedBodyCultivationSystem>();
            var userUid = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var victimUid = SEntMan.SpawnEntity("KsTestLegendaryBody", map.GridCoords);
            var swordUid = SEntMan.SpawnEntity("KsHeavenReliantSword", map.GridCoords);
            for (var stage = 0; stage < 4; stage++)
                bodySystem.AdvanceStage(victimUid);

            // Iron Shirt up.
            bodySystem.ApplyIronShirt(victimUid, System.TimeSpan.FromMinutes(1), 0.5f);
            var hardening = SEntMan.GetComponent<BodyHardeningComponent>(victimUid);
            var timing = Server.ResolveDependency<Robust.Shared.Timing.IGameTiming>();

            var hit = SEntMan.System<LegendarySystem>().LegendaryHit(userUid, victimUid, 20f, System.TimeSpan.Zero, swordUid);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(hit, Is.True);
                Assert.That(hardening.IronShirtEnd, Is.LessThanOrEqualTo(timing.CurTime), "The Heaven Reliant Sword didn't cut through Iron Shirt.");
                Assert.That(SEntMan.System<DamageableSystem>().GetTotalDamage(victimUid), Is.EqualTo(FixedPoint2.New(20)),
                    "A legendary blow was not true damage.");
            }
        });
    }

    [Test]
    public async Task TestLegendariesNeverRepeat()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var lootSystem = SEntMan.System<CultivationLootSystem>();
            var spawned = 0;
            while (spawned < 40 && lootSystem.SpawnLegendaryAt(map.MapCoords))
                spawned++;

            var counts = new System.Collections.Generic.Dictionary<string, int>();
            var query = SEntMan.EntityQueryEnumerator<MetaDataComponent>();
            while (query.MoveNext(out _, out var metaData))
            {
                if (metaData.EntityPrototype?.ID is { } id && id.StartsWith("Ks"))
                    counts[id] = counts.GetValueOrDefault(id) + 1;
            }

            using (Assert.EnterMultipleScope())
            {
                Assert.That(spawned, Is.InRange(2, 39), "The legendaries never ran out, or none spawned.");
                Assert.That(counts.GetValueOrDefault("KsGanjiang"), Is.LessThanOrEqualTo(1), "Ganjiang turned up twice.");
                Assert.That(counts.GetValueOrDefault("KsXuanyuanSword"), Is.LessThanOrEqualTo(1), "The Xuanyuan Sword turned up twice.");
                Assert.That(counts.GetValueOrDefault("KsRuyiJinguBang"), Is.LessThanOrEqualTo(1), "The Ruyi Jingu Bang turned up twice.");
            }
        });
    }

    [Test]
    public async Task TestTwinSwordsPair()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var handsSystem = SEntMan.System<SharedHandsSystem>();
            var legendarySystem = SEntMan.System<LegendarySystem>();
            var userUid = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var ganjiangUid = SEntMan.SpawnEntity("KsGanjiang", map.GridCoords);
            var moyeUid = SEntMan.SpawnEntity("KsMoye", map.GridCoords);
            var ganjiang = (ganjiangUid, SEntMan.GetComponent<TwinSwordComponent>(ganjiangUid));

            handsSystem.TryPickupAnyHand(userUid, ganjiangUid);
            var alone = legendarySystem.Paired(ganjiang, userUid);
            handsSystem.TryPickupAnyHand(userUid, moyeUid);
            var together = legendarySystem.Paired(ganjiang, userUid);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(alone, Is.False, "One sword counted as the pair.");
                Assert.That(together, Is.True, "Both swords in hand didn't count as the pair.");
            }
        });
    }

    [Test]
    public async Task TestHeavenCleavingStrokeCutsAWall()
    {
        var map = await Pair.CreateTestMap();
        EntityUid wallUid = default;
        EntityUid victimUid = default;

        await Server.WaitAssertion(() =>
        {
            var userUid = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var swordUid = SEntMan.SpawnEntity("KsHeavenReliantSword", map.GridCoords);
            victimUid = SEntMan.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(2f, 0f)));
            wallUid = SEntMan.SpawnEntity("WallReinforced", map.GridCoords.Offset(new Vector2(5f, 0f)));
            var ev = new HeavenCleavingStrokeActionEvent { Performer = userUid, Target = map.GridCoords.Offset(new Vector2(8f, 0f)) };
            SEntMan.EventBus.RaiseLocalEvent(swordUid, ev);
            Assert.That(ev.Handled, Is.True, "The stroke didn't go off.");
        });

        await RunTicksSync(5);

        await Server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(SEntMan.Deleted(wallUid), Is.True, "A reinforced wall survived the Heaven-Cleaving Stroke.");
                Assert.That(SEntMan.System<DamageableSystem>().GetTotalDamage(victimUid), Is.GreaterThan(FixedPoint2.Zero),
                    "The stroke passed through someone without cutting them.");
            }
        });
    }

    [Test]
    public async Task TestSecretOfTheSaberAndSword()
    {
        var map = await Pair.CreateTestMap();
        EntityUid saberUid = default;
        EntityUid swordUid = default;

        await Server.WaitAssertion(() =>
        {
            var userUid = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            saberUid = SEntMan.SpawnEntity("KsDragonSlayingSaber", map.GridCoords);
            swordUid = SEntMan.SpawnEntity("KsHeavenReliantSword", map.GridCoords);
            SEntMan.System<LegendarySystem>().RevealSecret(saberUid, swordUid, userUid);
        });

        await RunTicksSync(2);

        await Server.WaitAssertion(() =>
        {
            var manuals = 0;
            var query = SEntMan.EntityQueryEnumerator<InheritanceManualComponent>();
            while (query.MoveNext(out _, out _))
                manuals++;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(SEntMan.Deleted(saberUid) && SEntMan.Deleted(swordUid), Is.True, "The blades didn't shatter.");
                Assert.That(manuals, Is.EqualTo(2), "The Nine Yin Manual and the Wumu Yishu didn't fall out.");
            }
        });
    }

    [Test]
    public async Task TestInheritanceManualTeachesOnce()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var actionsSystem = SEntMan.System<SharedActionsSystem>();
            var firstUid = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var secondUid = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var manualUid = SEntMan.SpawnEntity("KsNineYinManual", map.GridCoords);

            SEntMan.EventBus.RaiseLocalEvent(manualUid, new UseInHandEvent(firstUid));
            SEntMan.EventBus.RaiseLocalEvent(manualUid, new UseInHandEvent(secondUid));

            bool Knows(EntityUid uid) => actionsSystem.GetActions(uid)
                .Any(action => SEntMan.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID == "ActionKsNineYinClaw");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(Knows(firstUid), Is.True, "The first reader learned nothing.");
                Assert.That(Knows(secondUid), Is.False, "The manual taught a second reader.");
            }
        });
    }

    [Test]
    public async Task TestGourdHearsAnswers()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var legendarySystem = SEntMan.System<LegendarySystem>();
            var victimUid = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var gourdUid = SEntMan.SpawnEntity("KsPurpleGoldGourd", map.GridCoords);
            var gourd = (gourdUid, SEntMan.GetComponent<PurpleGoldGourdComponent>(gourdUid));

            var answers = legendarySystem.IsAnswer("yes?", victimUid) && legendarySystem.IsAnswer("Who's asking", victimUid);
            var chatter = legendarySystem.IsAnswer("nice weather today", victimUid);
            legendarySystem.SuckIn(gourd, victimUid);
            var inside = SEntMan.System<Robust.Shared.Containers.SharedContainerSystem>().IsEntityInContainer(victimUid);
            legendarySystem.Release(gourd);
            var outside = !SEntMan.System<Robust.Shared.Containers.SharedContainerSystem>().IsEntityInContainer(victimUid);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(answers, Is.True, "A real answer didn't count.");
                Assert.That(chatter, Is.False, "Idle chatter counted as answering.");
                Assert.That(inside, Is.True, "The gourd didn't suck them in.");
                Assert.That(outside, Is.True, "Uncorking didn't let them out.");
            }
        });
    }

    [Test]
    public async Task TestBaguaMirrorSealsCultivation()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var cultivatorUid = SEntMan.SpawnEntity("KsTestLegendaryCultivator", map.GridCoords);
            SEntMan.System<SharedCultivationSystem>().AddQi(cultivatorUid, 1000f);
            var cultivationSystem = SEntMan.System<CultivationSystem>();
            var before = cultivationSystem.TryUseTechnique(cultivatorUid, 5f);

            SEntMan.System<LegendarySystem>().Seal(cultivatorUid, System.TimeSpan.FromSeconds(20));
            var sealedUse = cultivationSystem.TryUseTechnique(cultivatorUid, 5f);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(before, Is.True);
                Assert.That(sealedUse, Is.False, "A sealed cultivator still used a technique.");
            }
        });
    }

    [Test]
    public async Task TestRuyiGrowsAndPouchSwallows()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var legendarySystem = SEntMan.System<LegendarySystem>();
            var userUid = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var ruyiUid = SEntMan.SpawnEntity("KsRuyiJinguBang", map.GridCoords);
            legendarySystem.SetExtended((ruyiUid, SEntMan.GetComponent<RuyiStaffComponent>(ruyiUid)), true);

            var pouchUid = SEntMan.SpawnEntity("KsQiankunPouch", map.GridCoords);
            var looseUid = SEntMan.SpawnEntity("SheetSteel1", map.GridCoords);
            var ev = new QiankunSwallowActionEvent { Performer = userUid };
            SEntMan.EventBus.RaiseLocalEvent(pouchUid, ev);
            var stored = SEntMan.GetComponent<StorageComponent>(pouchUid).Container.ContainedEntities;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(SEntMan.GetComponent<MeleeWeaponComponent>(ruyiUid).Range, Is.GreaterThan(2f), "The staff didn't grow its reach.");
                Assert.That(stored.Contains(looseUid), Is.True, "The pouch didn't swallow a loose sheet.");
            }
        });
    }

    [Test]
    public async Task TestXuanyuanRejectsTheUnworthy()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var handsSystem = SEntMan.System<SharedHandsSystem>();
            var commonerUid = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var emperorUid = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            SEntMan.EnsureComponent<SonOfHeavenComponent>(emperorUid);

            handsSystem.TryPickupAnyHand(commonerUid, SEntMan.SpawnEntity("KsXuanyuanSword", map.GridCoords));
            handsSystem.TryPickupAnyHand(emperorUid, SEntMan.SpawnEntity("KsXuanyuanSword", map.GridCoords));
            var damageableSystem = SEntMan.System<DamageableSystem>();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(damageableSystem.GetTotalDamage(commonerUid), Is.GreaterThan(FixedPoint2.Zero), "The sword didn't burn an unworthy hand.");
                Assert.That(damageableSystem.GetTotalDamage(emperorUid), Is.EqualTo(FixedPoint2.Zero), "The sword burned the Son of Heaven.");
            }
        });
    }

    [Test]
    public async Task TestPagodaSealsAndReleases()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var legendarySystem = SEntMan.System<LegendarySystem>();
            var containerSystem = SEntMan.System<Robust.Shared.Containers.SharedContainerSystem>();
            var victimUid = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var pagodaUid = SEntMan.SpawnEntity("KsLinglongPagoda", map.GridCoords);
            var pagoda = (pagodaUid, SEntMan.GetComponent<LinglongPagodaComponent>(pagodaUid));

            legendarySystem.Seal(pagoda, victimUid);
            var sealedIn = containerSystem.IsEntityInContainer(victimUid);
            legendarySystem.ReleasePagoda(pagoda);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(sealedIn, Is.True, "The pagoda sealed nobody.");
                Assert.That(containerSystem.IsEntityInContainer(victimUid), Is.False, "The pagoda didn't let them out.");
            }
        });
    }

    [Test]
    public async Task TestSevenStarsNeedAllSeven()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var userUid = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var swordUid = SEntMan.SpawnEntity("KsSevenStarSword", map.GridCoords);
            var ev = new SevenStarsActionEvent { Performer = userUid, Target = map.GridCoords.Offset(new Vector2(4f, 0f)) };
            SEntMan.EventBus.RaiseLocalEvent(swordUid, ev);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(ev.Handled, Is.False, "The Seven Stars went off without a star lit.");
                Assert.That(SEntMan.System<LegendarySystem>().HonourDamage(userUid), Is.InRange(4f, 24f));
            }
        });
    }
}
