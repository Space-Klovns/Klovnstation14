using System;
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
    private const string ReloadedMapId = "KsForcedLobbyBackgroundTestReloadedMap";
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

- type: gameMap
  id: {ReloadedMapId}
  mapName: {ReloadedMapId}
  mapPath: /Maps/Test/empty.yml
  minPlayers: 0
  stations:
    Empty:
      stationProto: StandardNanotrasenStation
      components:
        - type: StationNameSetup
          mapNameTemplate: ""Empty""
";

    // The reloaded map as it is above, with or without a forced background line inserted.
    private static string ReloadedMap(string forcedLine) => $@"
- type: gameMap
  id: {ReloadedMapId}
  mapName: {ReloadedMapId}
  mapPath: /Maps/Test/empty.yml
  minPlayers: 0
{forcedLine}
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

    /// <summary>
    ///     The map manager keeps the instance it selected, so editing the selected map's forced background through a
    ///         prototype reload has to be picked up by reading the map afresh, both ways.
    /// </summary>
    [Test]
    public async Task ReloadingSelectedMapAppliesItsForcedBackground()
    {
        var configurationManager = Server.Resolve<IConfigurationManager>();
        var serverTicker = Server.System<GameTicker>();
        var clientTicker = Client.System<ClientGameTicker>();

        await Server.WaitPost(() => configurationManager.SetCVar(CCVars.GameMap, ReloadedMapId));
        await Pair.RunTicksSync(5);
        await Server.WaitAssertion(() =>
            Assert.That(serverTicker.LobbyBackground, Is.Not.EqualTo(_forcedBackground), "nothing forced yet"));

        await Server.WaitPost(() => ReloadPrototypes(ReloadedMap($"  forcedLobbyBackground: {_forcedBackground.Id}")));
        await Pair.RunTicksSync(5);
        await Server.WaitAssertion(() =>
            Assert.That(serverTicker.LobbyBackground, Is.EqualTo(_forcedBackground), "the reload added it"));
        await Client.WaitAssertion(() =>
            Assert.That(clientTicker.LobbyBackground, Is.EqualTo(_forcedBackground), "the client got it"));

        await Server.WaitPost(() => ReloadPrototypes(ReloadedMap(string.Empty)));
        await Pair.RunTicksSync(5);
        await Server.WaitAssertion(() =>
            Assert.That(serverTicker.LobbyBackground, Is.Not.EqualTo(_forcedBackground), "the reload removed it"));
    }

    private void ReloadPrototypes(string yaml)
    {
        var changes = new Dictionary<Type, HashSet<string>>();
        Server.ProtoMan.LoadString(yaml, overwrite: true, changed: changes);
        Server.ProtoMan.ResolveResults();
        Server.ProtoMan.ReloadPrototypes(changes);
    }
}
