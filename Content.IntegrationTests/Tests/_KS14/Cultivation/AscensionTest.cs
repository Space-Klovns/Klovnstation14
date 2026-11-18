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
            var qiBook = ascensionSystem.Choose(qiUid, qiScroll, CultivationPathOption.Fire);
            var bodyBook = ascensionSystem.Choose(bodyUid, bodyScroll, CultivationPathOption.Body);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(qiBook is { } q && SEntMan.GetComponent<MetaDataComponent>(q).EntityPrototype?.ID == "CultivationManualFire", Is.True,
                    "Choosing fire didn't hand over the fire manual.");
                Assert.That(bodyBook is { } b && SEntMan.GetComponent<MetaDataComponent>(b).EntityPrototype?.ID == "BodyManualMoldingArt", Is.True,
                    "Choosing the body didn't hand over the Body Molding Art.");
                Assert.That(ascensionSystem.SpeedBonus(), Is.GreaterThan(0f), "Ascension doesn't speed cultivation.");
            }

            // The books are what set them on their paths, as in any wuxia round.
            SEntMan.EnsureComponent<CultivatorComponent>(qiUid);
            SEntMan.System<Content.Shared._KS14.Cultivation.Body.SharedBodyCultivationSystem>().Commit((bodyUid, SEntMan.EnsureComponent<BodyCultivatorComponent>(bodyUid)));

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

    [Test]
    public async Task TestBodyBookCommitsAndForges()
    {
        var map = await Pair.CreateTestMap();
        EntityUid userUid = default, bookUid = default;

        await Server.WaitAssertion(() =>
        {
            var ascensionSystem = SEntMan.System<WuxiaAscensionRuleSystem>();
            userUid = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var scroll = SEntMan.SpawnEntity("KsPathChoice", map.GridCoords);
            bookUid = ascensionSystem.Choose(userUid, scroll, CultivationPathOption.Body)!.Value;
            Assert.That(SEntMan.System<Content.Shared.Hands.EntitySystems.SharedHandsSystem>().IsHolding(userUid, bookUid), Is.True,
                "The Body Molding Art isn't in hand.");
            SEntMan.System<Content.Shared.Interaction.SharedInteractionSystem>().UseInHandInteraction(userUid, bookUid);
        });

        await Server.WaitRunTicks(400);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.TryGetComponent<BodyCultivatorComponent>(userUid, out var body) && body.Committed, Is.True,
                "Reading the Body Molding Art didn't commit.");
            var forge = false;
            foreach (var action in SEntMan.System<Content.Shared.Actions.SharedActionsSystem>().GetActions(userUid))
                forge |= SEntMan.GetComponent<MetaDataComponent>(action.Owner).EntityPrototype?.ID == "ActionBodyForge";
            Assert.That(forge, Is.True, "There's no Forge the Body button.");
            Assert.That(body!.Tempering, Is.GreaterThan(0f), "Committing gave nothing to forge with.");
        });
    }
}
