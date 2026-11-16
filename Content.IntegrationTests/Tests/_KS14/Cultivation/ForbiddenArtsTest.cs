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
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Events;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._KS14.Cultivation;

/// <summary>
///     The Demonic Path: who can read the scripture, what it teaches, and the Five-Point Exploding Heart Palm.
/// </summary>
[TestFixture]
[TestOf(typeof(ForbiddenArtsSystem))]
public sealed class ForbiddenArtsTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Dirty = true };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: KsTestDemonicCultivator
  components:
  - type: Cultivator
";

    private const string Cultivator = "KsTestDemonicCultivator";

    [Test]
    public async Task TestDemonicPathTeachesByRealm()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var forbiddenArtsSystem = SEntMan.System<ForbiddenArtsSystem>();
            var cultivationSystem = SEntMan.System<CultivationSystem>();
            var sharedCultivationSystem = SEntMan.System<SharedCultivationSystem>();
            var discipleUid = SEntMan.SpawnEntity(Cultivator, map.GridCoords);
            var masterUid = SEntMan.SpawnEntity(Cultivator, map.GridCoords);

            forbiddenArtsSystem.MakeDemonic(discipleUid, master: false);
            forbiddenArtsSystem.MakeDemonic(masterUid, master: true);
            var discipleEntity = SEntity<CultivatorComponent>(discipleUid);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(sharedCultivationSystem.HasTechnique(discipleEntity.Comp, "ActionCultivationDevouringArt"), Is.True);
                Assert.That(sharedCultivationSystem.HasTechnique(discipleEntity.Comp, "ActionCultivationExplodingHeartPalm"), Is.False,
                    "A Golden Core art opened at Qi Condensation.");
                Assert.That(sharedCultivationSystem.HasTechnique(discipleEntity.Comp, ForbiddenArtsSystem.TransmitAction), Is.False,
                    "A disciple was given the means to pass the arts on.");
                Assert.That(sharedCultivationSystem.HasTechnique(SComp<CultivatorComponent>(masterUid), ForbiddenArtsSystem.TransmitAction), Is.True,
                    "A master could not pass the arts on.");
            }

            cultivationSystem.BreakThrough(discipleEntity);
            cultivationSystem.BreakThrough(discipleEntity);
            Assert.That(sharedCultivationSystem.HasTechnique(discipleEntity.Comp, "ActionCultivationExplodingHeartPalm"), Is.True,
                "Golden Core did not open the Exploding Heart Palm.");
        });
    }

    [Test]
    public async Task TestScriptureRejectsTheRighteous()
    {
        var map = await Pair.CreateTestMap();
        EntityUid readerUid = default;

        await Server.WaitPost(() =>
        {
            readerUid = SEntMan.SpawnEntity(Cultivator, map.GridCoords);
            var scriptureUid = SEntMan.SpawnEntity("KsDemonicScripture", map.GridCoords);
            SEntMan.System<SharedHandsSystem>().PickupOrDrop(readerUid, scriptureUid, checkActionBlocker: false);
            SEntMan.EventBus.RaiseLocalEvent(scriptureUid, new UseInHandEvent(readerUid));
        });

        await RunSeconds(13);

        await Server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(SEntMan.HasComponent<DemonicPathComponent>(readerUid), Is.False,
                    "Someone sworn to nothing comprehended the Demonic Path.");
                Assert.That(SEntMan.System<DamageableSystem>().GetTotalDamage(readerUid), Is.GreaterThan(FixedPoint2.Zero),
                    "Reading the scripture without being an antagonist did no harm.");
            }
        });
    }

    [Test]
    public async Task TestTransmissionMakesADisciple()
    {
        var map = await Pair.CreateTestMap();
        EntityUid mortalUid = default;

        await Server.WaitPost(() =>
        {
            mortalUid = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var slipUid = SEntMan.SpawnEntity("KsDemonicTransmission", map.GridCoords);
            SEntMan.System<SharedHandsSystem>().PickupOrDrop(mortalUid, slipUid, checkActionBlocker: false);
            SEntMan.EventBus.RaiseLocalEvent(slipUid, new UseInHandEvent(mortalUid));
        });

        await RunSeconds(11);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.TryGetComponent<DemonicPathComponent>(mortalUid, out var demonicComponent), Is.True,
                "Reading a master's transmission did not set the reader on the Demonic Path.");
            using (Assert.EnterMultipleScope())
            {
                Assert.That(demonicComponent!.Master, Is.False, "A transmission made a master, who could pass it on again.");
                Assert.That(SEntMan.HasComponent<CultivatorComponent>(mortalUid), Is.True, "The mortal was not awakened.");
            }
        });
    }

    [Test]
    public async Task TestTaintUnsettlesBreakthrough()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var forbiddenArtsSystem = SEntMan.System<ForbiddenArtsSystem>();
            var cultivationSystem = SEntMan.System<CultivationSystem>();
            var cultivatorUid = SEntMan.SpawnEntity(Cultivator, map.GridCoords);
            var cultivatorEntity = SEntity<CultivatorComponent>(cultivatorUid);

            forbiddenArtsSystem.MakeDemonic(cultivatorUid, master: false);
            var cleanReadiness = cultivationSystem.BreakthroughReadiness(cultivatorEntity);
            forbiddenArtsSystem.AddTaint(cultivatorUid, 12f);

            Assert.That(cultivationSystem.BreakthroughReadiness(cultivatorEntity), Is.EqualTo(cleanReadiness - 12),
                "Demonic taint did not weigh on the breakthrough.");
        });
    }

    [Test]
    public async Task TestExplodingHeartCountsSteps()
    {
        var map = await Pair.CreateTestMap();
        EntityUid victimUid = default;

        await Server.WaitAssertion(() =>
        {
            var forbiddenArtsSystem = SEntMan.System<ForbiddenArtsSystem>();
            var damageableSystem = SEntMan.System<DamageableSystem>();
            var transformSystem = SEntMan.System<SharedTransformSystem>();
            var strikerUid = SEntMan.SpawnEntity(Cultivator, map.GridCoords);
            victimUid = SEntMan.SpawnEntity("MobHuman", map.GridCoords);

            forbiddenArtsSystem.StrikeExplodingHeart(SEntity<CultivatorComponent>(strikerUid), victimUid, baseSteps: 3);
            Assert.That(SComp<ExplodingHeartComponent>(victimUid).StepsLeft, Is.EqualTo(3));

            for (var step = 1; step <= 2; step++)
                transformSystem.SetCoordinates(victimUid, map.GridCoords.Offset(new Vector2(step, 0)));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(SComp<ExplodingHeartComponent>(victimUid).StepsLeft, Is.EqualTo(1), "Steps were not counted.");
                Assert.That(damageableSystem.GetTotalDamage(victimUid), Is.EqualTo(FixedPoint2.Zero), "The heart burst early.");
            }

            transformSystem.SetCoordinates(victimUid, map.GridCoords.Offset(new Vector2(3, 0)));
        });

        await RunTicksSync(1);

        await Server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(SEntMan.System<DamageableSystem>().GetTotalDamage(victimUid), Is.GreaterThan(FixedPoint2.New(60)),
                    "The heart did not burst on the last step.");
                Assert.That(SEntMan.HasComponent<ExplodingHeartComponent>(victimUid), Is.False, "The points stayed after the heart burst.");
            }
        });
    }

    [Test]
    public async Task TestExplodingHeartUnsealed()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var forbiddenArtsSystem = SEntMan.System<ForbiddenArtsSystem>();
            var strikerUid = SEntMan.SpawnEntity(Cultivator, map.GridCoords);
            var sealedUid = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var meditatorUid = SEntMan.SpawnEntity(Cultivator, map.GridCoords);
            var striker = SEntity<CultivatorComponent>(strikerUid);

            forbiddenArtsSystem.StrikeExplodingHeart(striker, sealedUid, baseSteps: 5);
            forbiddenArtsSystem.StrikeExplodingHeart(striker, meditatorUid, baseSteps: 5);

            var sealEvent = new AcupointSealedEvent(strikerUid);
            SEntMan.EventBus.RaiseLocalEvent(sealedUid, ref sealEvent);

            var meditatedEvent = new CultivationMeditatedEvent();
            SEntMan.EventBus.RaiseLocalEvent(meditatorUid, ref meditatedEvent);
            Assert.That(SEntMan.HasComponent<ExplodingHeartComponent>(meditatorUid), Is.True, "One cycle of meditation was enough.");
            SEntMan.EventBus.RaiseLocalEvent(meditatorUid, ref meditatedEvent);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(sealEvent.Handled, Is.True, "Unsealing the heart also sealed the victim's voice.");
                Assert.That(SEntMan.HasComponent<ExplodingHeartComponent>(sealedUid), Is.False, "Acupoint Sealing did not undo the palm.");
                Assert.That(SEntMan.HasComponent<ExplodingHeartComponent>(meditatorUid), Is.False, "Two cycles of meditation did not undo the palm.");
            }
        });
    }
}
