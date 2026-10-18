#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Client._KS14.Atmos.FoamBlanket;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._KS14.Atmos.FoamBlanket;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Client.GameObjects;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using System.Numerics;
using ClientFoamBlanketSystem = Content.Client._KS14.Atmos.FoamBlanket.KsFoamBlanketSystem;

namespace Content.IntegrationTests.Tests._KS14.Atmos;

/// <summary>
///     An extinguisher shot the client fires itself, through the request a real client sends: the shooter's kept copy
///         of it lays the client's own blankets along its whole path, and they last until the server's arrive.
/// </summary>
[TestOf(typeof(KsPredictedFoamShotSystem))]
public sealed class KsPredictedFoamShotTest : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman"; // hands and combat mode, to fire a gun

    private const string Extinguisher = "FireExtinguisher";
    private const int Ticks = 75;

    /// <summary>
    ///     The tiles a shot along +x from the player can cross, given the extinguisher's spread.
    /// </summary>
    private static IEnumerable<Vector2i> PathTiles()
    {
        for (var x = -1; x <= 10; x++)
        {
            for (var y = -1; y <= 1; y++)
                yield return new Vector2i(x, y);
        }
    }

    /// <summary>
    ///     On the client, the shot lays a blanket on every tile it crosses, not only the first. Each one is the tile's
    ///         blanket from the moment it is laid until the server's replaces it, across every server state applied in
    ///         between, and no tile ever shows two.
    /// </summary>
    [Test]
    public async Task FiredShotLaysBlanketsThatLastUntilTheServersArrive()
    {
        // Well ahead of the server, so the server's blankets arrive several states after the client laid its own:
        //      an engine predicted spawn would be gone in between.
        var clientConfigurationManager = Client.ResolveDependency<IConfigurationManager>();
        await Client.WaitPost(() => clientConfigurationManager.SetCVar(CVars.NetPredictTickBias, 4));

        await Server.WaitPost(() =>
        {
            var plating = new Tile(TileMan[Plating].TileId);
            for (var x = -2; x <= 12; x++)
            {
                for (var y = -3; y <= 3; y++)
                    MapSystem.SetTile(MapData.Grid, new Vector2i(x, y), plating);
            }
        });

        var gunNetEntity = await PlaceInHands(Extinguisher); // and takes its safety off
        await SetCombatMode(true);
        await RunSeconds(2f); // the pickup cooldown

        var targetCoordinates = SEntMan.GetNetCoordinates(new EntityCoordinates(MapData.Grid.Owner, new Vector2(40.5f, 0.5f)));
        await Client.WaitPost(() =>
        {
            CEntMan.RaisePredictiveEvent(new RequestShootEvent
            {
                Gun = gunNetEntity,
                Coordinates = targetCoordinates,
            });
        });

        var clientGridUid = MapData.CGridUid;
        var placeholderSince = new Dictionary<Vector2i, int>();
        var serverSince = new Dictionary<Vector2i, int>();
        var blanketSince = new Dictionary<Vector2i, int>();
        var lostOn = new Dictionary<Vector2i, int>();
        var doubledOn = new Dictionary<Vector2i, int>();

        // A tick at a time, so a failure names the tick it happened on.
        for (var tick = 0; tick < Ticks; tick++)
        {
            await RunTicks(1);

            var currentTick = tick;
            await Client.WaitPost(() =>
            {
                var blanketSystem = CEntMan.System<ClientFoamBlanketSystem>();

                foreach (var tile in PathTiles())
                {
                    if (!blanketSystem.TryGetBlanket(clientGridUid, tile, out var blanket))
                    {
                        // Whatever kind it was: an engine predicted spawn would go at the next state.
                        if (blanketSince.ContainsKey(tile))
                            lostOn.TryAdd(tile, currentTick);
                        continue;
                    }

                    blanketSince.TryAdd(tile, currentTick);

                    if (CEntMan.HasComponent<KsFoamBlanketPlaceholderComponent>(blanket.Owner))
                        placeholderSince.TryAdd(tile, currentTick);
                    else if (!CEntMan.IsClientSide(blanket.Owner))
                        serverSince.TryAdd(tile, currentTick);
                }

                foreach (var (tile, count) in CountVisibleBlankets(clientGridUid))
                {
                    if (count > 1)
                        doubledOn.TryAdd(tile, currentTick);
                }
            });
        }

        var remainingPlaceholders = 0;
        await Client.WaitPost(() =>
        {
            var enumerator = CEntMan.AllEntityQueryEnumerator<KsFoamBlanketPlaceholderComponent>();
            while (enumerator.MoveNext(out _, out _))
                remainingPlaceholders++;
        });

        var handedOver = placeholderSince.Keys.Where(tile => serverSince.ContainsKey(tile)).ToList();
        var longestWait = handedOver.Count == 0 ? 0 : handedOver.Max(tile => serverSince[tile] - placeholderSince[tile]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(placeholderSince, Has.Count.GreaterThanOrEqualTo(3),
                $"The client laid its own blanket on {placeholderSince.Count} tiles of the shot's path, expected every tile it crossed.");
            Assert.That(lostOn, Is.Empty,
                "The client's blanket vanished before the server's replaced it, on tiles (tile: tick) " +
                string.Join(", ", lostOn.Select(pair => $"{pair.Key}: {pair.Value}")));
            Assert.That(doubledOn, Is.Empty,
                "A tile showed two blankets at once, on tiles (tile: tick) " +
                string.Join(", ", doubledOn.Select(pair => $"{pair.Key}: {pair.Value}")));
            Assert.That(handedOver, Has.Count.GreaterThanOrEqualTo(3),
                "The server's blankets did not take over the tiles the client laid its own on.");
            Assert.That(longestWait, Is.GreaterThan(2),
                "No client blanket waited more than a couple of ticks for the server's, so this proves nothing about it outliving states.");
            Assert.That(remainingPlaceholders, Is.Zero, "Client blankets were left behind once the server's had arrived.");
        }
    }

    /// <summary>
    ///     The visible blankets on the client, by tile; a hidden one is on its way out.
    /// </summary>
    private Dictionary<Vector2i, int> CountVisibleBlankets(EntityUid clientGridUid)
    {
        var mapSystem = CEntMan.System<SharedMapSystem>();
        var mapGridComponent = CEntMan.GetComponent<MapGridComponent>(clientGridUid);
        var counts = new Dictionary<Vector2i, int>();

        var enumerator = CEntMan.AllEntityQueryEnumerator<KsFoamBlanketComponent, SpriteComponent, TransformComponent>();
        while (enumerator.MoveNext(out var uid, out _, out var spriteComponent, out var transformComponent))
        {
            if (!spriteComponent.Visible || CEntMan.IsQueuedForDeletion(uid) || transformComponent.GridUid != clientGridUid)
                continue;

            var tile = mapSystem.LocalToTile(clientGridUid, mapGridComponent, transformComponent.Coordinates);
            counts[tile] = counts.GetValueOrDefault(tile) + 1;
        }

        return counts;
    }
}
