#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._KS14.Fluids;
using Content.Client._KS14.PredictedSpawning;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server.Fluids.EntitySystems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids;
using Content.Shared.Fluids.Components;
using Robust.Client.GameObjects;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Fluids;

/// <summary>
///     Spills a player's own vapor makes, predicted by their client: a placeholder puddle on a bare tile until the
///         server's arrives, and, into a puddle the server already sent, a top-up that is only drawn.
/// </summary>
[TestOf(typeof(KsPredictedSpillSystem))]
public sealed class KsPredictedSpillTest : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman"; // hands, to hold the spray bottle

    private static readonly EntProtoId SprayBottlePrototype = "SprayBottle";
    private static readonly EntProtoId VaporPrototype = "Vapor";
    private static readonly ProtoId<ReagentPrototype> SpaceLubeReagent = "SpaceLube"; // spills on every tile it crosses
    private static readonly ProtoId<ReagentPrototype> WaterReagent = "Water"; // spills only into a puddle already there

    private const int Ticks = 75;

    /// <summary>
    ///     The tiles a spray along +x from the player can cross.
    /// </summary>
    private static IEnumerable<Vector2i> PathTiles()
    {
        for (var x = -1; x <= 6; x++)
        {
            for (var y = -1; y <= 1; y++)
                yield return new Vector2i(x, y);
        }
    }

    private async Task FloorPath()
    {
        await Server.WaitPost(() =>
        {
            var plating = new Tile(TileMan[Plating].TileId);
            for (var x = -2; x <= 8; x++)
            {
                for (var y = -3; y <= 3; y++)
                    MapSystem.SetTile(MapData.Grid, new Vector2i(x, y), plating);
            }
        });
    }

    /// <summary>
    ///     The client's puddles on a tile that are shown: its own waiting placeholder, the server's, or both.
    /// </summary>
    private (int Placeholders, int ServerPuddles) CountShownPuddles(EntityUid clientGridUid, Vector2i tile)
    {
        var mapSystem = CEntMan.System<SharedMapSystem>();
        var mapGridComponent = CEntMan.GetComponent<MapGridComponent>(clientGridUid);

        var placeholders = 0;
        var serverPuddles = 0;
        var anchored = mapSystem.GetAnchoredEntitiesEnumerator(clientGridUid, mapGridComponent, tile);
        while (anchored.MoveNext(out var uid))
        {
            if (!CEntMan.HasComponent<PuddleComponent>(uid) ||
                CEntMan.IsQueuedForDeletion(uid.Value) ||
                !CEntMan.TryGetComponent<SpriteComponent>(uid, out var spriteComponent) ||
                !spriteComponent.Visible)
                continue;

            if (CEntMan.HasComponent<KsTilePlaceholderComponent>(uid))
                placeholders++;
            else
                serverPuddles++;
        }

        return (placeholders, serverPuddles);
    }

    /// <summary>
    ///     A player's own spray of space lube puts a puddle on each tile it crosses at once, on their client, ahead of
    ///         the server's. Each one stays across the states applied until the server's puddle for its tile arrives
    ///         and takes over; no tile ever shows two, or none in between; and none are left behind.
    /// </summary>
    [Test]
    public async Task SprayLaysPuddlesThatLastUntilTheServersArrive()
    {
        // Well ahead of the server, so the server's puddles arrive several states after the client laid its own.
        var clientConfigurationManager = Client.ResolveDependency<IConfigurationManager>();
        await Client.WaitPost(() => clientConfigurationManager.SetCVar(CVars.NetPredictTickBias, 4));

        await FloorPath();

        var sprayNetEntity = await PlaceInHands(SprayBottlePrototype.Id);
        await Server.WaitPost(() =>
        {
            var sprayUid = SEntMan.GetEntity(sprayNetEntity);
            var solutionContainerSystem = SEntMan.System<SharedSolutionContainerSystem>();
            Assert.That(solutionContainerSystem.TryGetSolution(sprayUid, SEntMan.GetComponent<SprayComponent>(sprayUid).Solution, out var solution));
            solutionContainerSystem.TryAddReagent(solution!.Value, SpaceLubeReagent.Id, FixedPoint2.New(30), out _);
        });
        await RunSeconds(2f); // the pickup cooldown

        var target = SEntMan.GetNetCoordinates(new EntityCoordinates(MapData.Grid.Owner, new Vector2(3.5f, 0.5f)));
        await SetKey(EngineKeyFunctions.Use, BoundKeyState.Down, target, cursorEntity: NetEntity.Invalid);
        await SetKey(EngineKeyFunctions.Use, BoundKeyState.Up, target, cursorEntity: NetEntity.Invalid);

        var clientGridUid = MapData.CGridUid;
        var placeholderSince = new Dictionary<Vector2i, int>();
        var serverSince = new Dictionary<Vector2i, int>();
        var shownSince = new Dictionary<Vector2i, int>();
        var lostOn = new Dictionary<Vector2i, int>();
        var doubledOn = new Dictionary<Vector2i, int>();
        var emptiedOn = new Dictionary<Vector2i, int>();

        // A tick at a time, so a failure names the tick it happened on.
        for (var tick = 0; tick < Ticks; tick++)
        {
            await RunTicks(1);

            var currentTick = tick;
            await Client.WaitPost(() =>
            {
                var solutionContainerSystem = CEntMan.System<SharedSolutionContainerSystem>();
                foreach (var tile in PathTiles())
                {
                    var (placeholders, serverPuddles) = CountShownPuddles(clientGridUid, tile);
                    if (placeholders + serverPuddles == 0)
                    {
                        if (shownSince.ContainsKey(tile) && !serverSince.ContainsKey(tile))
                            lostOn.TryAdd(tile, currentTick);
                        continue;
                    }

                    shownSince.TryAdd(tile, currentTick);
                    if (placeholders + serverPuddles > 1)
                        doubledOn.TryAdd(tile, currentTick);
                    if (placeholders > 0)
                        placeholderSince.TryAdd(tile, currentTick);
                    if (serverPuddles > 0)
                        serverSince.TryAdd(tile, currentTick);
                }

                // A placeholder that lost its solution to a state reset is an empty puddle.
                var enumerator = CEntMan.AllEntityQueryEnumerator<KsTilePlaceholderComponent, PuddleComponent, TransformComponent>();
                while (enumerator.MoveNext(out var uid, out _, out var puddleComponent, out var transformComponent))
                {
                    if (!solutionContainerSystem.ResolveSolution(uid, puddleComponent.SolutionName, ref puddleComponent.Solution, out var solution) ||
                        solution.Volume <= FixedPoint2.Zero)
                    {
                        emptiedOn.TryAdd(CEntMan.System<SharedMapSystem>().LocalToTile(clientGridUid,
                            CEntMan.GetComponent<MapGridComponent>(clientGridUid), transformComponent.Coordinates), currentTick);
                    }
                }
            });
        }

        var remainingPlaceholders = 0;
        await Client.WaitPost(() =>
        {
            var enumerator = CEntMan.AllEntityQueryEnumerator<KsTilePlaceholderComponent, PuddleComponent>();
            while (enumerator.MoveNext(out _, out _, out _))
                remainingPlaceholders++;
        });

        var handedOver = placeholderSince.Keys.Where(tile => serverSince.ContainsKey(tile)).ToList();
        var longestWait = handedOver.Count == 0 ? 0 : handedOver.Max(tile => serverSince[tile] - placeholderSince[tile]);

        static string Describe(Dictionary<Vector2i, int> tiles) => string.Join(", ", tiles.Select(pair => $"{pair.Key}: {pair.Value}"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(placeholderSince, Has.Count.GreaterThanOrEqualTo(2),
                $"The client spilled its own puddle on {placeholderSince.Count} tiles of the spray's path, expected each it crossed.");
            Assert.That(lostOn, Is.Empty, "The client's puddle vanished before the server's replaced it, on tiles (tile: tick) " + Describe(lostOn));
            Assert.That(doubledOn, Is.Empty, "A tile showed two puddles at once, on tiles (tile: tick) " + Describe(doubledOn));
            Assert.That(emptiedOn, Is.Empty, "A client puddle lost its solution, on tiles (tile: tick) " + Describe(emptiedOn));
            Assert.That(handedOver, Has.Count.GreaterThanOrEqualTo(2), "The server's puddles did not take over the tiles the client spilled on.");
            Assert.That(longestWait, Is.GreaterThan(2),
                "No client puddle waited more than a couple of ticks for the server's, so this proves nothing about it outliving states.");
            Assert.That(remainingPlaceholders, Is.Zero, "Client puddles were left behind once the server's had arrived.");
        }
    }

    /// <summary>
    ///     Tile reactions the client has act only for its own kept vapor. Anything else on the client that runs a
    ///         reagent's tile reactions (a mop's absorbent, from predicted code re-run every re-predicted tick) still
    ///         does nothing there, as before the client had any: no puddle, and nothing used up.
    /// </summary>
    [Test]
    public async Task ReactionsOutsideAKeptVaporDoNothingOnTheClient()
    {
        await FloorPath();
        await RunTicks(5); // so the client has the floor: nothing reacts with an empty tile

        var tile = new Vector2i(2, 0);
        await Client.WaitAssertion(() =>
        {
            var tileRef = CEntMan.System<SharedMapSystem>().GetTileRef(MapData.CGridUid, CEntMan.GetComponent<MapGridComponent>(MapData.CGridUid), tile);
            var solution = new Solution(SpaceLubeReagent.Id, FixedPoint2.New(5));

            CEntMan.System<SharedPuddleSystem>().DoTileReactions(tileRef, solution);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(solution.Volume, Is.EqualTo(FixedPoint2.New(5)), "A tile reaction used the solution up on the client.");
                Assert.That(CountShownPuddles(MapData.CGridUid, tile), Is.EqualTo((0, 0)), "A tile reaction spilled a puddle on the client.");
            }
        });
    }

    /// <summary>
    ///     A player's own vapor spilling into a puddle the server already sent draws the puddle bigger at once, while
    ///         the puddle's solution stays the server's, so no state can shrink it back and flicker it. It stays drawn
    ///         bigger across the states that follow, until the server's own spill lands (and is then drawn once, not
    ///         twice), or, if the server never spills, until it expires.
    /// </summary>
    [Test]
    public async Task TopUpIsDrawnWithoutFlickerUntilTheServersLands([Values] bool serverSpills)
    {
        await FloorPath();

        var tile = new Vector2i(2, 0);
        EntityUid serverPuddleUid = default;
        await Server.WaitPost(() =>
        {
            var tileRef = MapSystem.GetTileRef(MapData.Grid, tile);
            Assert.That(SEntMan.System<PuddleSystem>().TrySpillAt(tileRef, new Solution(WaterReagent.Id, FixedPoint2.New(5)), out serverPuddleUid, sound: false));
        });
        await RunTicks(10);

        var clientGridUid = MapData.CGridUid;
        var clientPuddleUid = CEntMan.GetEntity(SEntMan.GetNetEntity(serverPuddleUid));
        var serverDrawnBefore = 0f;
        var networkedVolumeBefore = FixedPoint2.Zero;
        var drawnWithTopUp = 0f;
        var pendingUnits = 0f;

        float ServerDrawn() => CEntMan.System<SharedAppearanceSystem>().TryGetData<float>(clientPuddleUid, PuddleVisuals.CurrentVolume, out var value) ? value : -1f;
        FixedPoint2 NetworkedVolume()
        {
            var puddleComponent = CEntMan.GetComponent<PuddleComponent>(clientPuddleUid);
            Assert.That(CEntMan.System<SharedSolutionContainerSystem>().ResolveSolution(clientPuddleUid, puddleComponent.SolutionName, ref puddleComponent.Solution, out var solution));
            return solution!.Volume;
        }

        await Client.WaitAssertion(() =>
        {
            serverDrawnBefore = ServerDrawn();
            networkedVolumeBefore = NetworkedVolume();
            Assert.That(serverDrawnBefore, Is.GreaterThan(0f), "The server's puddle never reached the client.");

            // This client's kept vapor of water, as its own spray would make, on the puddle's tile.
            var vaporUid = CEntMan.SpawnAtPosition(VaporPrototype, new EntityCoordinates(clientGridUid, tile.X + 0.5f, tile.Y + 0.5f));
            CEntMan.AddComponent<KsKeptPredictedSpawnComponent>(vaporUid);
            var vaporSolution = new Entity<SolutionComponent>(vaporUid, CEntMan.GetComponent<SolutionComponent>(vaporUid));
            CEntMan.System<SharedSolutionContainerSystem>().TryAddSolution(vaporSolution, new Solution(WaterReagent.Id, FixedPoint2.New(4)));

            CEntMan.System<SharedVaporSystem>().Update(0f);

            pendingUnits = 4f - (float)vaporSolution.Comp.Solution.Volume;
            drawnWithTopUp = CEntMan.System<KsPredictedSpillSystem>().GetDrawnVolume(clientPuddleUid, ServerDrawn());

            using (Assert.EnterMultipleScope())
            {
                Assert.That(pendingUnits, Is.GreaterThan(0f), "The vapor spilled nothing into the puddle.");
                Assert.That(drawnWithTopUp, Is.GreaterThan(serverDrawnBefore), "The puddle is not drawn bigger for what the client spilled into it.");
                Assert.That(NetworkedVolume(), Is.EqualTo(networkedVolumeBefore), "The client changed the server's puddle, which the next state would undo.");
            }

            CEntMan.DeleteEntity(vaporUid);
        });

        if (serverSpills)
        {
            var spilled = FixedPoint2.New(pendingUnits);
            await Server.WaitPost(() =>
            {
                var tileRef = MapSystem.GetTileRef(MapData.Grid, tile);
                Assert.That(SEntMan.System<PuddleSystem>().TrySpillAt(tileRef, new Solution(WaterReagent.Id, spilled), out _, sound: false, tileReact: false));
            });
        }

        // The states that follow: drawn bigger until the server's spill lands, and never twice over.
        var shrankOn = -1;
        var doubleCountedOn = -1;
        var landedOn = -1;
        for (var tick = 0; tick < 20; tick++)
        {
            await RunTicks(1);

            var currentTick = tick;
            await Client.WaitPost(() =>
            {
                var serverDrawn = ServerDrawn();
                var drawn = CEntMan.System<KsPredictedSpillSystem>().GetDrawnVolume(clientPuddleUid, serverDrawn);
                var landed = serverDrawn > serverDrawnBefore + (drawnWithTopUp - serverDrawnBefore) * 0.5f;

                if (landed && landedOn < 0)
                    landedOn = currentTick;
                if (!landed && drawn < drawnWithTopUp - 0.001f && shrankOn < 0)
                    shrankOn = currentTick;
                if (drawn > drawnWithTopUp + (drawnWithTopUp - serverDrawnBefore) * 0.5f && doubleCountedOn < 0)
                    doubleCountedOn = currentTick;
            });
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(shrankOn, Is.EqualTo(-1), $"The top-up stopped being drawn on tick {shrankOn}, before the server's spill landed.");
            Assert.That(doubleCountedOn, Is.EqualTo(-1), $"The top-up was drawn on top of the server's spill on tick {doubleCountedOn}.");
            Assert.That(landedOn >= 0, Is.EqualTo(serverSpills), serverSpills ? "The server's spill never reached the client." : "The puddle grew with no spill.");
        }

        // Past the top-up's lifetime: whatever happened, the puddle is drawn as the server has it.
        await RunSeconds((float)KsPredictedSpillSystem.PredictionLifetime.TotalSeconds + 0.5f);
        await Client.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(CEntMan.HasComponent<KsPredictedPuddleTopUpComponent>(clientPuddleUid), Is.False, "The top-up never went.");
                Assert.That(CEntMan.System<KsPredictedSpillSystem>().GetDrawnVolume(clientPuddleUid, ServerDrawn()), Is.EqualTo(ServerDrawn()),
                    "The puddle is not drawn as the server has it.");
            }
        });
    }
}
