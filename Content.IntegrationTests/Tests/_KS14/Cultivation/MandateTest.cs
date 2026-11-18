#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Cultivation.Components;
using Content.Server._KS14.Cultivation.Systems;
using Content.Shared._KS14.Cultivation;
using Content.Shared._KS14.Cultivation.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mind;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._KS14.Cultivation;

/// <summary>
///     Heaven grants its Mandate, protects its ruler, withdraws it when they die, and gives it to whoever raises the seal.
/// </summary>
[TestFixture]
[TestOf(typeof(MandateSystem))]
public sealed class MandateTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Dirty = true };

    [Test]
    public async Task TestMandateFallsAndIsClaimed()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var mandateSystem = SEntMan.System<MandateSystem>();
            var damageableSystem = SEntMan.System<DamageableSystem>();
            var mindSystem = SEntMan.System<SharedMindSystem>();
            var rulerUid = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var mortalUid = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var claimantUid = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            foreach (var uid in new[] { rulerUid, mortalUid, claimantUid })
                mindSystem.TransferTo(mindSystem.CreateMind(null).Owner, uid);

            Assert.That(mandateSystem.Grant(rulerUid, null, son: true, job: "Captain"), Is.Not.Null, "Heaven granted nothing.");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(SEntMan.HasComponent<SonOfHeavenComponent>(rulerUid), Is.True, "The Captain isn't the Son of Heaven.");
                Assert.That(SEntMan.GetComponent<MandateOfHeavenComponent>(rulerUid).CourtAction, Is.Not.Null, "There's no Imperial Court button.");
                Assert.That(mandateSystem.PowersOf(SEntMan.GetComponent<MandateOfHeavenComponent>(rulerUid)).Count(), Is.EqualTo(8),
                    "The Son of Heaven doesn't have all eight powers.");
            }

            // A power from the court: ennoble someone, and it cools down.
            var ruler = (rulerUid, SEntMan.GetComponent<MandateOfHeavenComponent>(rulerUid));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(mandateSystem.Use(ruler, MandatePower.Ennoble, mortalUid), Is.True, "Ennoble failed.");
                Assert.That(SEntMan.HasComponent<ImperialNobleComponent>(mortalUid), Is.True, "Nobody was ennobled.");
                Assert.That(mandateSystem.Use(ruler, MandatePower.Ennoble, claimantUid), Is.False, "Ennoble didn't cool down.");
            }

            // Heaven's Protection: the same blow hurts a ruler less.
            var blow = Content.Server._KS14.Cultivation.Systems.CultivationSystem.Damage("Blunt", 20f);
            damageableSystem.TryChangeDamage(mortalUid, blow, origin: claimantUid);
            var mortalTook = damageableSystem.GetTotalDamage(mortalUid);
            var rulerTook = FixedPoint2.Zero;
            for (var attempt = 0; attempt < 5 && rulerTook == FixedPoint2.Zero; attempt++)
            {
                // Heaven's Fortune may make a blow miss entirely; keep striking until one lands.
                damageableSystem.TryChangeDamage(rulerUid, blow, origin: claimantUid);
                rulerTook = damageableSystem.GetTotalDamage(rulerUid);
            }

            Assert.That(rulerTook, Is.GreaterThan(FixedPoint2.Zero).And.LessThan(mortalTook), "Heaven's Protection didn't soften the blow.");

            // Heaven turns aside the first fatal blow...
            var fatal = Content.Server._KS14.Cultivation.Systems.CultivationSystem.Damage("Blunt", 500f);
            damageableSystem.TryChangeDamage(rulerUid, fatal, ignoreResistances: true);
            Assert.That(SEntMan.System<Content.Shared.Mobs.Systems.MobStateSystem>().IsAlive(rulerUid), Is.True, "Heaven's Protection didn't turn aside the fatal blow.");

            // ...but not the second: the ruler dies, the Mandate is withdrawn, and the seal falls.
            damageableSystem.TryChangeDamage(rulerUid, fatal, ignoreResistances: true);
            JadeSealComponent? seal = null;
            EntityUid sealUid = default;
            var seals = SEntMan.EntityQueryEnumerator<JadeSealComponent>();
            while (seals.MoveNext(out var uid, out var component))
            {
                seal = component;
                sealUid = uid;
            }

            using (Assert.EnterMultipleScope())
            {
                Assert.That(SEntMan.HasComponent<MandateOfHeavenComponent>(rulerUid), Is.False, "A dead ruler kept the Mandate.");
                Assert.That(SEntMan.HasComponent<SonOfHeavenComponent>(rulerUid), Is.False, "A dead ruler stayed the Son of Heaven.");
                Assert.That(seal, Is.Not.Null, "No seal fell.");
                Assert.That(seal!.SonOfHeaven, Is.True, "The Heirloom Seal forgot it was the realm's.");
                Assert.That(SEntMan.HasComponent<RegicideCurseComponent>(claimantUid), Is.True, "The one who struck the ruler down isn't a regicide.");
            }

            // A regicide can't claim it; once the curse lifts, they can.
            Assert.That(mandateSystem.Claim(claimantUid, (sealUid, seal)), Is.False, "A regicide claimed the seal.");
            SEntMan.RemoveComponent<RegicideCurseComponent>(claimantUid);
            Assert.That(mandateSystem.Claim(claimantUid, (sealUid, seal)), Is.True, "Raising the seal failed.");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(SEntMan.TryGetComponent<MandateOfHeavenComponent>(claimantUid, out var claimed) && claimed.SonOfHeaven, Is.True,
                    "Raising the seal didn't make them the Son of Heaven.");
                Assert.That(claimed!.Lineage, Has.Count.EqualTo(1), "The seal forgot who held it before.");
                Assert.That(claimed.GrantedJob, Is.Null, "A seized Mandate cares about job paperwork.");
            }
        });
    }
}
