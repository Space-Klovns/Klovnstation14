using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Content.Client.GameTicking.Managers;
using Content.IntegrationTests.Fixtures;
using Content.Server.GameTicking;
using Content.Shared.CCVar;
using Content.Shared.GameTicking.Prototypes;
using Content.Shared.Maps;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.GameTicking;

/// <summary>
///     A map with <see cref="GameMapPrototype.ForcedLobbyBackground"/> shows that background while it is selected,
///         leaving it rerolls, a background that isn't <see cref="LobbyBackgroundPrototype.RandomlySelectable"/> is
///         never rolled, and the client always shows what the server picked.
/// </summary>
[TestOf(typeof(GameTicker))]
public sealed class KsForcedLobbyBackgroundTest : GameTest
{
    private const string ForcingMapId = "KsForcedLobbyBackgroundTestMap";
    private const string UnforcedMapId = "Empty";
    private const int Cycles = 20;

    private readonly ProtoId<LobbyBackgroundPrototype> _forcedBackground = "KsForcedLobbyBackgroundTestBackground";

    [TestPrototypes]
    private static readonly string Prototypes = $@"
- type: lobbyBackground
  id: KsForcedLobbyBackgroundTestBackground
  background: /Textures/_KS14/LobbyScreens/198hnk.png
  randomlySelectable: false

- type: gameMap
  id: {ForcingMapId}
  mapName: {ForcingMapId}
  mapPath: /Maps/Test/empty.yml
  minPlayers: 0
  forcedLobbyBackground: KsForcedLobbyBackgroundTestBackground
  stations:
    Empty:
      stationProto: StandardNanotrasenStation
      components:
        - type: StationNameSetup
          mapNameTemplate: ""Empty""
";

    public override PoolSettings PoolSettings => new() { Connected = true, InLobby = true, Dirty = true };

    [Test]
    public async Task ForcedBackgroundFollowsSelectedMap()
    {
        var configurationManager = Server.Resolve<IConfigurationManager>();
        var serverTicker = Server.System<GameTicker>();
        var clientTicker = Client.System<ClientGameTicker>();

        // The roll pool is private; checking it directly beats hoping a random roll lands on the excluded one.
        var rollPool = (List<ProtoId<LobbyBackgroundPrototype>>)typeof(GameTicker)
            .GetField("_lobbyBackgrounds", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(serverTicker)!;
        Assert.That(rollPool, Is.Not.Empty, "other backgrounds can be rolled");
        Assert.That(rollPool, Does.Not.Contain(_forcedBackground), "not randomly selectable");

        // Each cycle rerolls once, so a background that random rolls could pick would turn up within a few.
        for (var i = 0; i < Cycles; i++)
        {
            await Server.WaitPost(() => configurationManager.SetCVar(CCVars.GameMap, ForcingMapId));
            await Pair.RunTicksSync(5);
            await Server.WaitAssertion(() =>
                Assert.That(serverTicker.LobbyBackground, Is.EqualTo(_forcedBackground), "the map forces it"));
            await Client.WaitAssertion(() =>
                Assert.That(clientTicker.LobbyBackground, Is.EqualTo(_forcedBackground), "the client got it"));

            await Server.WaitPost(() => configurationManager.SetCVar(CCVars.GameMap, UnforcedMapId));
            await Pair.RunTicksSync(5);

            ProtoId<LobbyBackgroundPrototype>? rolledBackground = null;
            await Server.WaitAssertion(() =>
            {
                rolledBackground = serverTicker.LobbyBackground;
                Assert.That(rolledBackground, Is.Not.EqualTo(_forcedBackground), "rerolled, and never into the forced one");
            });
            await Client.WaitAssertion(() =>
                Assert.That(clientTicker.LobbyBackground, Is.EqualTo(rolledBackground), "the client got the roll"));
        }
    }
}
