using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Cultivation.Systems;
using Content.Shared._KS14.Cultivation.Components;
using Content.Shared._KS14.Cultivation.Systems;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._KS14.Cultivation;

/// <summary>
///     Techniques show: every effect prototype exists, a cast grows with realm, and auras follow their state.
/// </summary>
[TestFixture]
[TestOf(typeof(CultivationVisualsSystem))]
public sealed class CultivationVisualsTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Dirty = true };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: KsTestVisualsCultivator
  components:
  - type: Cultivator
";

    private int CountEffects(string prototypeId)
    {
        var count = 0;
        var query = SEntMan.AllEntityQueryEnumerator<MetaDataComponent>();
        while (query.MoveNext(out _, out var metaData))
        {
            if (metaData.EntityPrototype?.ID == prototypeId)
                count++;
        }

        return count;
    }

    [Test]
    public async Task TestEveryEffectExists()
    {
        await Server.WaitAssertion(() =>
        {
            var prototypeManager = Server.ResolveDependency<Robust.Shared.Prototypes.IPrototypeManager>();
            foreach (var kind in new[] { "Wave", "WaveBig", "Spark", "Motes", "Aura", "Dome", "Sigil" })
            {
                foreach (var colour in System.Enum.GetValues<FxColour>())
                    Assert.That(prototypeManager.HasIndex<Robust.Shared.Prototypes.EntityPrototype>(CultivationVisualsSystem.EffectId(kind, colour)), Is.True,
                        $"No {kind} effect in {colour}.");
            }

            // The effects ported from SS13.
            foreach (var id in new[] { "KsFxBuddhaPalm", "KsFxBuddhaPalmBig", "KsFxBuddhaPalmHuge", "KsFxPalmShadow", "KsFxPalmShadowBig",
                         "KsFxPalmShadowHuge", "KsFxCrater", "KsFxCraterBig", "KsFxCraterHuge", "KsFxRubble", "KsFxVoidRift",
                         "KsFxSwordQiImpact", "KsFxThornSplinters", "KsFxVines", "KsFxMoltenStep", "KsFxGoldenBell", "KsFxWaterBubble",
                         "KsFxMirrorBubble", "KsFxDharmaIdol", "KsFxHeartDemonAura", "KsFxBeastAura", "KsFxStormCloud", "KsFxFallingRock", "KsFxTide" })
            {
                Assert.That(prototypeManager.HasIndex<Robust.Shared.Prototypes.EntityPrototype>(id), Is.True, $"No {id}.");
            }
        });
    }

    [Test]
    public async Task TestBuddhaPalmFallsThenSlams()
    {
        var map = await Pair.CreateTestMap();
        EntityUid victimUid = default;

        await Server.WaitAssertion(() =>
        {
            var cultivationSystem = SEntMan.System<CultivationSystem>();
            var uid = SEntMan.SpawnEntity("KsTestVisualsCultivator", map.GridCoords.Offset(new System.Numerics.Vector2(6f, 0f)));
            var cultivator = SEntity<CultivatorComponent>(uid);
            for (var realm = 0; realm < 3; realm++)
                cultivationSystem.BreakThrough(cultivator);

            SEntMan.System<SharedCultivationSystem>().AddQi(uid, 1000f);
            // The test map has one floor tile: the palm comes down on it, the caster standing just off it.
            var target = map.GridCoords;
            victimUid = SEntMan.SpawnEntity("MobHuman", target);
            var ev = new Content.Shared._KS14.Cultivation.BuddhaPalmActionEvent { Performer = uid, Target = target };
            SEntMan.EventBus.RaiseLocalEvent(uid, ev);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(ev.Handled, Is.True, "The palm wasn't called down.");
                Assert.That(CountEffects("KsFxBuddhaPalmBig"), Is.EqualTo(1), "No palm fell out of the sky.");
                Assert.That(CountEffects("KsFxPalmShadowBig"), Is.EqualTo(1), "The palm cast no shadow.");
                Assert.That(SEntMan.System<Content.Shared.Damage.Systems.DamageableSystem>().GetTotalDamage(victimUid),
                    Is.EqualTo(Content.Shared.FixedPoint.FixedPoint2.Zero), "The palm hit before it fell.");
            }
        });

        await RunSeconds(1.5f);

        await Server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(SEntMan.System<Content.Shared.Damage.Systems.DamageableSystem>().GetTotalDamage(victimUid),
                    Is.GreaterThan(Content.Shared.FixedPoint.FixedPoint2.Zero), "The palm landed on nobody.");
                Assert.That(CountEffects("KsFxCraterBig") + CountEffects("KsFxCraterHuge"), Is.GreaterThanOrEqualTo(1), "The palm left no crater.");
            }
        });
    }

    [Test]
    public async Task TestCastGrowsWithRealm()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var cultivationSystem = SEntMan.System<CultivationSystem>();
            var uid = SEntMan.SpawnEntity("KsTestVisualsCultivator", map.GridCoords);
            SEntMan.System<SharedCultivationSystem>().AddQi(uid, 1000f);

            Assert.That(cultivationSystem.TryUseTechnique(uid, 5f), Is.True);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(CountEffects("KsFxCastQi"), Is.EqualTo(1), "A cast showed no ring of qi.");
                Assert.That(CountEffects("KsFxCastBigQi") + CountEffects("KsBodyShockwave"), Is.Zero, "A beginner's cast shook the room.");
            }

            var cultivator = SEntity<CultivatorComponent>(uid);
            for (var realm = 0; realm < 3; realm++)
                cultivationSystem.BreakThrough(cultivator);

            var shockwavesBefore = CountEffects("KsBodyShockwave");
            SEntMan.System<SharedCultivationSystem>().AddQi(uid, 1000f);
            cultivationSystem.TryUseTechnique(uid, 5f);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(CountEffects("KsFxCastBigQi"), Is.GreaterThanOrEqualTo(1), "A Nascent Soul's cast was no greater than a beginner's.");
                Assert.That(CountEffects("KsBodyShockwave"), Is.GreaterThan(shockwavesBefore), "The air did not ripple at the peak.");
            }
        });
    }

    [Test]
    public async Task TestAuraFollowsItsState()
    {
        var map = await Pair.CreateTestMap();
        EntityUid uid = default;

        await Server.WaitAssertion(() =>
        {
            uid = SEntMan.SpawnEntity("KsTestVisualsCultivator", map.GridCoords);
            SEntMan.System<CultivationStanceSystem>().ApplyGoldenBellQi(uid, System.TimeSpan.FromSeconds(1.5));
        });

        await RunSeconds(0.6f);
        await Server.WaitAssertion(() =>
            Assert.That(CountEffects("KsFxGoldenBell"), Is.EqualTo(1), "Golden Bell Qi showed no bell."));

        await RunSeconds(2.5f);
        await Server.WaitAssertion(() =>
            Assert.That(CountEffects("KsFxGoldenBell"), Is.Zero, "The bell outlived the technique."));
    }
}
