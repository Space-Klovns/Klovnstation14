using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Cultivation.Components;
using Content.Server._KS14.Cultivation.Systems;
using Content.Shared._KS14.Cultivation;
using Content.Shared._KS14.Cultivation.Components;
using Content.Shared._KS14.Cultivation.Systems;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._KS14.Cultivation;

/// <summary>
///     Batch E: feng shui on a cultivation mat, the guqin, heavenly treasures, the ancestral ring and round-start loot.
/// </summary>
[TestFixture]
[TestOf(typeof(CultivationSiteSystem))]
public sealed class CultivationSitesTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Dirty = true };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: KsTestSiteCultivator
  components:
  - type: Cultivator
";

    [Test]
    public async Task TestMatAndElementsFeedTheLaw()
    {
        var map = await Pair.CreateTestMap();
        EntityUid uid = default;

        await Server.WaitAssertion(() =>
        {
            var siteSystem = SEntMan.System<CultivationSiteSystem>();
            uid = SEntMan.SpawnEntity("KsTestSiteCultivator", map.GridCoords);
            SEntMan.System<SharedCultivationSystem>().TryLearnLaw(uid, "KsEarth");
            var cultivator = SEntity<CultivatorComponent>(uid);

            var bare = siteSystem.Evaluate(cultivator);
            SEntMan.SpawnEntity("KsCultivationMat", map.GridCoords);
            var mat = siteSystem.Evaluate(cultivator);

            // Earth for an Earth law: gold, an ore, a statue. Identical things count twice at most.
            for (var i = 0; i < 3; i++)
                SEntMan.SpawnEntity("IngotGold1", map.GridCoords);
            SEntMan.SpawnEntity("GoldOre1", map.GridCoords);
            var earth = siteSystem.Evaluate(cultivator);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(bare.HasMat, Is.False);
                Assert.That(mat.HasMat, Is.True, "Sitting on the mat didn't count.");
                Assert.That(mat.Multiplier, Is.GreaterThan(bare.Multiplier), "The mat didn't help.");
                Assert.That(earth.ElementPoints[CultivationElement.Earth], Is.EqualTo(3), "Earth wasn't counted, or identical sheets weren't capped.");
                Assert.That(earth.Multiplier, Is.GreaterThan(mat.Multiplier), "Earth didn't nourish an Earth law.");
                Assert.That(earth.ReadinessBonus, Is.GreaterThan(mat.ReadinessBonus), "Earth didn't steady the breakthrough.");
            }
        });
    }

    [Test]
    public async Task TestOpposingElementSuppresses()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var siteSystem = SEntMan.System<CultivationSiteSystem>();
            var uid = SEntMan.SpawnEntity("KsTestSiteCultivator", map.GridCoords);
            SEntMan.System<SharedCultivationSystem>().TryLearnLaw(uid, "KsEarth");
            var cultivator = SEntity<CultivatorComponent>(uid);
            SEntMan.SpawnEntity("KsCultivationMat", map.GridCoords);
            var plain = siteSystem.Evaluate(cultivator);

            // Wood overcomes earth.
            SEntMan.SpawnEntity("PottedPlantRandom", map.GridCoords);
            SEntMan.SpawnEntity("TableWood", map.GridCoords);
            var suppressed = siteSystem.Evaluate(cultivator);

            Assert.That(suppressed.Multiplier, Is.LessThan(plain.Multiplier), "Wood didn't suppress an Earth law.");
        });
    }

    [Test]
    public async Task TestGuqinTeachesPlayerAndListener()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var playerUid = SEntMan.SpawnEntity("KsTestSiteCultivator", map.GridCoords);
            var listenerUid = SEntMan.SpawnEntity("KsTestSiteCultivator", map.GridCoords.Offset(new Vector2(1f, 0f)));
            var guqinUid = SEntMan.SpawnEntity("KsGuqin", map.GridCoords);
            var guqinSystem = SEntMan.System<GuqinSystem>();

            guqinSystem.Resonate((guqinUid, SEntMan.GetComponent<GuqinComponent>(guqinUid)), playerUid);
            var listenerInsight = SEntMan.GetComponent<CultivatorComponent>(listenerUid).Insight;

            // A second wave straight away: the listener has only just learned from it.
            guqinSystem.Resonate((guqinUid, SEntMan.GetComponent<GuqinComponent>(guqinUid)), playerUid);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(SEntMan.GetComponent<CultivatorComponent>(playerUid).Insight, Is.GreaterThan(0f), "Playing taught the player nothing.");
                Assert.That(listenerInsight, Is.GreaterThan(0f), "Listening taught nothing.");
                Assert.That(SEntMan.GetComponent<CultivatorComponent>(listenerUid).Insight, Is.EqualTo(listenerInsight),
                    "A listener learned twice in a row without a pause.");
            }
        });
    }

    [Test]
    public async Task TestSpiritHerbFeedsCultivatorsAndHealsMortals()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var treasureSystem = SEntMan.System<HeavenlyTreasureSystem>();
            var damageableSystem = SEntMan.System<DamageableSystem>();

            var cultivatorUid = SEntMan.SpawnEntity("KsTestSiteCultivator", map.GridCoords);
            var cultivatorHerb = SEntMan.SpawnEntity("KsSpiritHerb", map.GridCoords);
            treasureSystem.Eat((cultivatorHerb, SEntMan.GetComponent<SpiritHerbComponent>(cultivatorHerb)), cultivatorUid);

            var mortalUid = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            damageableSystem.TryChangeDamage(mortalUid, CultivationSystem.Damage("Blunt", 40f));
            var mortalHerb = SEntMan.SpawnEntity("KsSpiritHerb", map.GridCoords);
            treasureSystem.Eat((mortalHerb, SEntMan.GetComponent<SpiritHerbComponent>(mortalHerb)), mortalUid);

            var cultivator = SEntMan.GetComponent<CultivatorComponent>(cultivatorUid);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(cultivator.Insight, Is.GreaterThan(0f), "The herb gave no insight.");
                Assert.That(cultivator.Qi, Is.EqualTo(SEntMan.System<SharedCultivationSystem>().GetMaxQi(cultivator)).Within(0.01f),
                    "The herb didn't fill the dantian.");
                Assert.That(damageableSystem.GetTotalDamage(mortalUid), Is.LessThan(FixedPoint2.New(40)), "The herb didn't heal a mortal.");
            }
        });
    }

    [Test]
    public async Task TestRingOffersItsMaster()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var ringSystem = SEntMan.System<AncestralRingSystem>();
            var cultivatorUid = SEntMan.SpawnEntity("KsTestSiteCultivator", map.GridCoords);
            var ringUid = SEntMan.SpawnEntity("KsAncestralRing", map.GridCoords);
            ringSystem.Awaken((ringUid, SEntMan.GetComponent<AncestralRingComponent>(ringUid)));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(SEntMan.HasComponent<Content.Server.Ghost.Roles.Components.GhostRoleComponent>(ringUid), Is.True,
                    "Nobody was asked to play the master.");
                Assert.That(ringSystem.InsightMultiplier(cultivatorUid), Is.EqualTo(1f), "An empty ring helped someone who isn't carrying it.");
            }
        });
    }

    [Test]
    public async Task TestLootIsHidden()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            SEntMan.System<CultivationLootSystem>().SpawnLoot(map.Grid.Owner);

            var rings = 0;
            var query = SEntMan.EntityQueryEnumerator<AncestralRingComponent>();
            while (query.MoveNext(out _, out _))
                rings++;

            Assert.That(rings, Is.EqualTo(1), "The ancestral ring wasn't hidden.");
        });
    }
}
