#nullable enable
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Cultivation;
using Content.Server._KS14.Cultivation.Systems;
using Content.Server.GameTicking;
using Content.Shared._KS14.Cultivation;
using Content.Shared._KS14.Cultivation.Body;
using Content.Shared._KS14.Cultivation.Components;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Cultivation;

/// <summary>
///     Wuxia: Ascension. Players choose a path from the scroll, and the first to the peak becomes the Supreme Sovereign.
/// </summary>
[TestFixture]
[TestOf(typeof(WuxiaAscensionRuleSystem))]
public sealed class AscensionTest : GameTest
{
    private static readonly EntProtoId AscensionRule = "KsWuxiaAscension";

    public override PoolSettings PoolSettings => new() { Dirty = true };

    [Test]
    public async Task TestPathsAndPeak()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var ascensionSystem = SEntMan.System<WuxiaAscensionRuleSystem>();
            SEntMan.System<GameTicker>().StartGameRule(AscensionRule);
            Assert.That(ascensionSystem.Active(), Is.True, "The Ascension rule didn't start.");

            var qiUid = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var bodyUid = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var qiScroll = SEntMan.SpawnEntity("KsPathChoice", map.GridCoords);
            var bodyScroll = SEntMan.SpawnEntity("KsPathChoice", map.GridCoords);
            ascensionSystem.Choose(qiUid, qiScroll, body: false);
            ascensionSystem.Choose(bodyUid, bodyScroll, body: true);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(SEntMan.HasComponent<CultivatorComponent>(qiUid), Is.True, "The path of qi didn't awaken anyone.");
                Assert.That(SEntMan.TryGetComponent<BodyCultivatorComponent>(bodyUid, out var body) && body.Committed, Is.True,
                    "The path of the body didn't commit anyone.");
                Assert.That(SEntMan.HasComponent<CultivatorComponent>(bodyUid), Is.False, "The body cultivator also got qi.");
                Assert.That(ascensionSystem.SpeedBonus(), Is.GreaterThan(0f), "Ascension doesn't speed cultivation.");
            }

            var peak = new CultivationPeakReachedEvent(bodyUid);
            SEntMan.EventBus.RaiseEvent(EventSource.Local, ref peak);
            var second = new CultivationPeakReachedEvent(qiUid);
            SEntMan.EventBus.RaiseEvent(EventSource.Local, ref second);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(SEntMan.HasComponent<SupremeSovereignComponent>(bodyUid), Is.True, "The first to the peak isn't Sovereign.");
                Assert.That(SEntMan.HasComponent<SupremeSovereignComponent>(qiUid), Is.False, "A second Sovereign was crowned.");
            }
        });
    }
}
