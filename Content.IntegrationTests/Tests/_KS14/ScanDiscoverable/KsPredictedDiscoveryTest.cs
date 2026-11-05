#nullable enable
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.ScanDiscoverable.Base;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Localization;

namespace Content.IntegrationTests.Tests._KS14.ScanDiscoverable;

/// <summary>
///     Discovering something runs on the client too, ahead of the server: a real click, with the client in front.
/// </summary>
[TestOf(typeof(KsScanDiscoverableSystem))]
public sealed class KsPredictedDiscoveryTest : InteractionTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestPredictedDiscoverable
  name: undiscovered thing
  components:
  - type: KsScanDiscoverable
    trueName: ks-scan-discovered-crude-oil-geyser
    discoveryPopupLoc: ks-specific-geyser-discovered
  - type: KsScanTriggerOnDiscovered
    keyOut: discovered
  - type: AddComponentsOnTrigger
    keysIn: [discovered]
    components:
    - type: KsRadarInterest
      data: {}
  - type: SpawnOnTrigger
    keysIn: [discovered]
    proto: KsTestPredictedDiscoveryReward
    predicted: true

- type: entity
  id: KsTestPredictedDiscoveryReward
  name: reward

- type: entity
  id: KsTestPredictedDiscoveringScanner
  name: test scanner
  components:
  - type: Item
  - type: KsDiscoveringScanner
";

    protected override string PlayerPrototype => "MobHuman"; // hands, to hold the scanner

    private const string TrueName = "ks-scan-discovered-crude-oil-geyser";
    private const string RewardPrototype = "KsTestPredictedDiscoveryReward";
    private const int Ticks = 30;

    private static int CountRewards(IEntityManager entityManager)
    {
        var count = 0;
        var query = entityManager.AllEntityQueryEnumerator<MetaDataComponent>();
        while (query.MoveNext(out var metaDataComponent))
        {
            if (metaDataComponent.EntityPrototype?.ID == RewardPrototype)
                count++;
        }

        return count;
    }

    [Test]
    public async Task DiscoveringIsPredictedAndKeepsTheName()
    {
        // Well ahead of the server, so states the server sent before it saw the click are applied after the client ran it.
        var clientConfigurationManager = Client.ResolveDependency<IConfigurationManager>();
        await Client.WaitPost(() =>
        {
            clientConfigurationManager.SetCVar(CVars.NetPredictTickBias, 4);
            clientConfigurationManager.SetCVar(KsCCVars.ClientLocale, "ru-RU");
        });

        await SpawnTarget("KsTestPredictedDiscoverable");
        await PlaceInHands("KsTestPredictedDiscoveringScanner");
        await RunSeconds(2f); // the pickup cooldown

        var clientUid = CTarget!.Value;
        var expectedName = Client.ResolveDependency<ILocalizationManager>().GetString(TrueName);

        await SetKey(EngineKeyFunctions.Use, BoundKeyState.Down);
        await SetKey(EngineKeyFunctions.Use, BoundKeyState.Up);

        Assert.That(SEntMan.HasComponent<KsScanDiscoverableComponent>(STarget!.Value), "The server has already run, so this isn't testing the client's prediction.");
        Assert.That(CEntMan.HasComponent<KsScanDiscoverableComponent>(clientUid), Is.False, "The click wasn't predicted.");

        // A tick at a time, so a failure names the tick it happened on.
        for (var tick = 0; tick < Ticks; tick++)
        {
            Assert.That(CEntMan.GetComponent<MetaDataComponent>(clientUid).EntityName, Is.EqualTo(expectedName), $"The client lost the name on tick {tick}.");
            await RunTicks(1);
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(SEntMan.HasComponent<KsScanDiscoverableComponent>(STarget!.Value), Is.False, "The server never ran the click.");
            Assert.That(CEntMan.HasComponent<KsScanDiscoverableComponent>(clientUid), Is.False, "The server's state took the discovery back.");
            Assert.That(CEntMan.GetComponent<KsDiscoveredNameComponent>(clientUid).Name.Id, Is.EqualTo(TrueName));
            Assert.That(CEntMan.GetComponent<MetaDataComponent>(clientUid).EntityName, Is.EqualTo(expectedName));
            Assert.That(CountRewards(SEntMan), Is.EqualTo(1), "The server didn't trigger exactly once.");
            Assert.That(CountRewards(CEntMan), Is.EqualTo(1), "Replaying the click left the client with extra rewards.");
        }
    }
}
