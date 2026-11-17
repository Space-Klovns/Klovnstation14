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
            Assert.That(CountEffects("KsFxDomeGold"), Is.EqualTo(1), "Golden Bell Qi showed no dome."));

        await RunSeconds(2.5f);
        await Server.WaitAssertion(() =>
            Assert.That(CountEffects("KsFxDomeGold"), Is.Zero, "The dome outlived the bell."));
    }
}
