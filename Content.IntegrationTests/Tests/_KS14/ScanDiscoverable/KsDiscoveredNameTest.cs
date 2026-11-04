using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.ScanDiscoverable.Base;
using Content.Shared.Interaction;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.ScanDiscoverable;

/// <summary>
///     Something discovered shows its name in each client's language, whatever language the server runs in.
/// </summary>
[TestOf(typeof(KsScanDiscoverableSystem))]
public sealed class KsDiscoveredNameTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestDiscoverable
  name: undiscovered thing
  components:
  - type: KsScanDiscoverable
    trueName: ks-scan-discovered-crude-oil-geyser

- type: entity
  id: KsTestDiscoveringScanner
  name: test scanner
  components:
  - type: KsDiscoveringScanner
";

    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private const string TrueName = "ks-scan-discovered-crude-oil-geyser";

    // Instance fields: these ids exist only in the test prototypes above.
    private readonly EntProtoId _discoverablePrototype = "KsTestDiscoverable";
    private readonly EntProtoId _scannerPrototype = "KsTestDiscoveringScanner";

    [Test]
    public async Task ClientsShowTheNameInTheirOwnLanguage()
    {
        var testMap = await Pair.CreateTestMap();
        var clientLocalization = Client.ResolveDependency<ILocalizationManager>();
        var configuration = Client.ResolveDependency<IConfigurationManager>();

        await Client.WaitPost(() => configuration.SetCVar(KsCCVars.ClientLocale, "ru-RU"));

        EntityUid serverUid = default;
        await Server.WaitPost(() =>
        {
            serverUid = Server.EntMan.SpawnEntity(_discoverablePrototype, testMap.GridCoords);
            var scannerUid = Server.EntMan.SpawnEntity(_scannerPrototype, testMap.GridCoords);
            var ev = new InteractUsingEvent(scannerUid, scannerUid, serverUid, testMap.GridCoords);
            Server.EntMan.EventBus.RaiseLocalEvent(serverUid, ev);
        });
        await Pair.RunTicksSync(5);

        var serverName = Server.EntMan.GetComponent<MetaDataComponent>(serverUid).EntityName;
        var clientUid = Client.EntMan.GetEntity(Server.EntMan.GetNetEntity(serverUid));

        await Client.WaitAssertion(() =>
        {
            Assert.That(Client.EntMan.GetComponent<MetaDataComponent>(clientUid).EntityName, Is.EqualTo(clientLocalization.GetString(TrueName)));
            Assert.That(Client.EntMan.GetComponent<MetaDataComponent>(clientUid).EntityName, Is.Not.EqualTo(serverName),
                "A Russian client shows the English server's name.");

            configuration.SetCVar(KsCCVars.ClientLocale, "en-US");
            Assert.That(Client.EntMan.GetComponent<MetaDataComponent>(clientUid).EntityName, Is.EqualTo(serverName),
                "Changing language left the old name showing.");
        });
    }
}
