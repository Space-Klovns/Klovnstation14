using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Server.GameTicking.Rules.Components;
using Content.Shared.GameTicking.Components;
using Content.Shared._KS14.Scenario.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Zombies;
using Content.Server.RoundEnd;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.GameTicking.Rules;

[RegisterComponent]
public sealed partial class ScenarioRuleComponent : Component
{
}
public sealed class ScenarioSystem : GameRuleSystem<ScenarioRuleComponent>
{
    [Dependency] private readonly RoundEndSystem _roundEnd = default!;

    public override void Initialize()
    {
        base.Initialize();
        //SubscribeLocalEvent<ScenarioRuleComponent, RuleLoadedGridsEvent>(OnRuleLoadedGrids);

        //syndie checks
        SubscribeLocalEvent<ScenarioSyndieComponent, ComponentRemove>(OnSyndieCompRemove);
        SubscribeLocalEvent<ScenarioSyndieComponent, MobStateChangedEvent>(OnSyndieMobstateChanged);
        SubscribeLocalEvent<ScenarioSyndieComponent, EntityZombifiedEvent>(OnSyndieZombified);

        //NT checks
        SubscribeLocalEvent<ScenarioNtComponent, ComponentRemove>(OnNtCompRemove);
        SubscribeLocalEvent<ScenarioNtComponent, MobStateChangedEvent>(OnNtMobstateChanged);
        SubscribeLocalEvent<ScenarioNtComponent, EntityZombifiedEvent>(OnNtZombified);

    }

    protected override void Added(EntityUid uid, ScenarioRuleComponent component, GameRuleComponent gameRule, GameRuleAddedEvent args)
    {
        base.Added(uid, component, gameRule, args);

        // ScenarioSystem only handles map-loaded scenarios via OnRuleLoadedGrids event
        // Map loading and RuleLoadedGridsEvent is handled by LoadMapRuleSystem
    }

    private void OnSyndieCompRemove(EntityUid uid, ScenarioSyndieComponent component, ComponentRemove args)
    {
        CheckRoundShouldEnd();
    }

    private void OnSyndieMobstateChanged(EntityUid uid, ScenarioSyndieComponent component, MobStateChangedEvent ev)
    {
        if (ev.NewMobState == MobState.Dead)
            CheckRoundShouldEnd();
    }

    private void OnSyndieZombified(EntityUid uid, ScenarioSyndieComponent component, ref EntityZombifiedEvent args)
    {
        RemCompDeferred(uid, component);
    }

    private void OnNtCompRemove(EntityUid uid, ScenarioNtComponent component, ComponentRemove args)
    {
        CheckRoundShouldEnd();
    }

    private void OnNtMobstateChanged(EntityUid uid, ScenarioNtComponent component, MobStateChangedEvent ev)
    {
        if (ev.NewMobState == MobState.Dead)
            CheckRoundShouldEnd();
    }

    private void OnNtZombified(EntityUid uid, ScenarioNtComponent component, ref EntityZombifiedEvent args)
    {
        RemCompDeferred(uid, component);
    }

    /*private void OnRuleLoadedGrids(EntityUid uid, ScenarioRuleComponent component, ref RuleLoadedGridsEvent args)
    {
        if (args.Grids.Count == 0)
            return;

        if (!_entityManager.TryGetComponent<LoadMapRuleComponent>(uid, out var loadMapRule))
            return;

        if (loadMapRule.GameMap == null)
            return;

        if (!_protoManager.TryIndex(loadMapRule.GameMap, out GameMapPrototype? gameMapProto))
            return;

        if (gameMapProto?.ScenarioConfig == null)
        {
            Log.Warning($"Scenario: Map {loadMapRule.GameMap} has no ScenarioConfig");
            return;
        }

        var scenarioConfig = gameMapProto.ScenarioConfig;
        var targetGrid = args.Grids[0];
        var gridComp = _entityManager.GetComponent<MapGridComponent>(targetGrid);
        var gridMapId = _transform.GetMapId(new Entity<TransformComponent?>(targetGrid, null));

        Log.Info($"Scenario: Processing map {loadMapRule.GameMap} with config Shape={scenarioConfig.MapShape} Size={scenarioConfig.MapSize}");

        BoxMapWithWalls(targetGrid, gridComp, scenarioConfig.MapShape, scenarioConfig.MapSize, scenarioConfig.CanyonWidth);
        SpawnStructures(targetGrid, gridComp, scenarioConfig.Structures, scenarioConfig.MapSize, component);

        if (component.ObjectiveEntities.Count > 0)
        {
            component.ObjectiveExpiration = Timing.CurTime + TimeSpan.FromMinutes(30);
        }

        Log.Info($"Scenario: Map scenario setup complete");
    }

    private void BoxMapWithWalls(EntityUid gridUid, MapGridComponent gridComp, MapShape shape, int mapSize, int canyonWidth)
    {
        var wallProto = "RockWallInvincible";
        var halfSize = mapSize / 2;
        var halfCanyonWidth = canyonWidth / 2;

        if (shape == MapShape.Circle)
        {
            // Spawn walls in a circle around the perimeter
            var wallRadius = mapSize / 2;
            var circumference = (int)(2 * MathF.PI * wallRadius);

            for (int i = 0; i < circumference; i++)
            {
                var angle = (i / (float)circumference) * MathF.PI * 2;
                var x = (int)(MathF.Cos(angle) * wallRadius);
                var y = (int)(MathF.Sin(angle) * wallRadius);

                SpawnWallAtTile(gridUid, gridComp, wallProto, new Vector2i(x, y));
            }

            Log.Info($"Scenario: Spawned circular perimeter walls");
        }
        else if (shape == MapShape.Canyon)
        {
            // Spawn walls at canyon edges
            for (int x = -halfSize; x <= halfSize; x++)
            {
                // Top wall
                SpawnWallAtTile(gridUid, gridComp, wallProto, new Vector2i(x, halfCanyonWidth + 1));
                // Bottom wall
                SpawnWallAtTile(gridUid, gridComp, wallProto, new Vector2i(x, -halfCanyonWidth - 1));
            }

            Log.Info("Scenario: Spawned canyon edge walls");
        }
    }

    private void SpawnWallAtTile(EntityUid gridUid, MapGridComponent gridComp, string proto, Vector2i tile)
    {
        try
        {
            var coords = _mapSystem.GridTileToLocal(gridUid, gridComp, tile);
            _entityManager.SpawnAtPosition(proto, coords);
        }
        catch
        {
            // Silent fail - wall spawning is not critical
        }
    }

    private List<EntityUid> SpawnStructures(EntityUid gridUid, MapGridComponent gridComp, List<ScenarioStructure> structures, int mapSize, ScenarioRuleComponent component)
    {
        var loadedGrids = new List<EntityUid> { gridUid };

        foreach (var structure in structures)
        {
            var loadedGrid = SpawnStructure(gridUid, gridComp, structure, mapSize, component);
            if (loadedGrid != null)
            {
                loadedGrids.Add(loadedGrid.Value);
            }
        }

        return loadedGrids;
    }

    private EntityUid? SpawnStructure(EntityUid gridUid, MapGridComponent gridComp, ScenarioStructure structure, int mapSize, ScenarioRuleComponent component)
    {
        try
        {
            Vector2i targetTile;

            if (structure.RandomLocation)
            {
                // Find a random collision-free tile using the anchorable system
                var maxAttempts = 50;
                var attempts = 0;

                do
                {
                    var x = _random.Next(-mapSize / 4, mapSize / 4);
                    var y = _random.Next(-mapSize / 4, mapSize / 4);
                    targetTile = new Vector2i(x, y);
                    attempts++;

                    if (_anchorable.TileFree(new Entity<MapGridComponent>(gridUid, gridComp), targetTile, (int)CollisionGroup.MachineLayer, (int)CollisionGroup.MachineLayer))
                    {
                        break;
                    }
                } while (attempts < maxAttempts);

                if (attempts >= maxAttempts)
                {
                    Log.Warning($"Scenario: Could not find free tile for {structure.ProtoId ?? structure.GridPath?.ToString() ?? "structure"} after {maxAttempts} attempts");
                    return null;
                }
            }
            else
            {
                // Use specified coordinates
                targetTile = new Vector2i(structure.Coordinates.X, structure.Coordinates.Y);
            }

            var coords = _mapSystem.GridTileToLocal(gridUid, gridComp, targetTile);
            EntityUid? spawnedUid = null;

            if (structure.GridPath != null)
            {
                var opts = DeserializationOptions.Default with { InitializeMaps = true };
                var mapId = _transform.GetMapId(new Entity<TransformComponent?>(gridUid, null));
                if (!_mapLoader.TryLoadGrid(mapId, structure.GridPath.Value, out var loadedGrid, opts, coords.Position))
                {
                    Log.Warning($"Scenario: Failed to load grid from {structure.GridPath}");
                    return null;
                }

                spawnedUid = loadedGrid.Value.Owner;
                Log.Info($"Scenario: Loaded grid {structure.GridPath} at tile {targetTile}");
            }
            else if (!string.IsNullOrWhiteSpace(structure.ProtoId))
            {
                spawnedUid = _entityManager.SpawnAtPosition(structure.ProtoId, coords);
                Log.Info($"Scenario: Spawned {structure.ProtoId} at tile {targetTile}");
            }
            else
            {
                Log.Warning($"Scenario: Structure definition missing proto and gridPath");
                return null;
            }

            if (structure.Objective && spawnedUid != null)
            {
                EnsureComp<ScenarioObjectiveComponent>(spawnedUid.Value);
                component.ObjectiveEntities.Add(spawnedUid.Value);
                SpawnObjectivePinpointer(spawnedUid.Value, coords);
            }

            return spawnedUid;
        }
        catch (Exception e)
        {
            Log.Warning($"Scenario: Failed to spawn {structure.ProtoId ?? structure.GridPath?.ToString() ?? "structure"}: {e.Message}");
            return null;
        }
    }

    private void SpawnObjectivePinpointer(EntityUid objectiveUid, EntityCoordinates coords)
    {
        try
        {
            var pinpointerUid = _entityManager.SpawnAtPosition("PinpointerUniversal", coords);
            _pinpointer.SetTarget(new Entity<PinpointerComponent?>(pinpointerUid, null), objectiveUid);
            Log.Info($"Scenario: Spawned default pinpointer {pinpointerUid} for objective {objectiveUid}");
        }
        catch (Exception e)
        {
            Log.Warning($"Scenario: Failed to spawn pinpointer for objective {objectiveUid}: {e.Message}");
        }
    }

    protected override void ActiveTick(EntityUid uid, ScenarioRuleComponent component, GameRuleComponent gameRule, float frameTime)
    {
        base.ActiveTick(uid, component, gameRule, frameTime);

        if (component.ObjectiveExpiration == null)
            return;

        var objectiveAlive = false;
        foreach (var objective in component.ObjectiveEntities)
        {
            if (!TerminatingOrDeleted(objective))
            {
                objectiveAlive = true;
                break;
            }
        }

        if (!objectiveAlive)
        {
            _roundEnd.RequestRoundEnd(checkCooldown: false, text: "scenario-round-end-objective-destroyed", name: "scenario-round-end-objective-destroyed", cantRecall: true);
            component.ObjectiveEntities.Clear();
            return;
        }

        if (Timing.CurTime >= component.ObjectiveExpiration.Value)
        {
            _roundEnd.RequestRoundEnd(checkCooldown: false, text: "scenario-round-end-objective-defended", name: "scenario-round-end-objective-defended", cantRecall: true);
            component.ObjectiveEntities.Clear();
        }
    }*/
    private void CheckRoundShouldEnd()
    {
        /*var query = QueryActiveRules();
        while (query.MoveNext(out var uid, out _, out var nukeops, out _))
        {
            CheckRoundShouldEnd((uid, nukeops));
        }*/
    }

    /*private void CheckRoundShouldEnd(Entity<NukeopsRuleComponent> ent)
    {
        var nukeops = ent.Comp;

        if (nukeops.WinType == WinType.CrewMajor || nukeops.WinType == WinType.OpsMajor) // Skip this if the round's victor has already been decided.
            return;

        // If there are any nuclear bombs that are active, immediately return. We're not over yet.
        foreach (var nuke in EntityQuery<NukeComponent>())
        {
            if (nuke.Status == NukeStatus.ARMED)
                return;
        }

        var shuttle = GetShuttle((ent, ent));

        MapId? shuttleMapId = Exists(shuttle)
            ? Transform(shuttle.Value).MapID
            : null;

        MapId? targetStationMap = null;
        if (nukeops.TargetStation != null && TryComp(nukeops.TargetStation, out StationDataComponent? data))
        {
            var grid = data.Grids.FirstOrNull();
            targetStationMap = grid != null
                ? Transform(grid.Value).MapID
                : null;
        }

        // Check if there are nuke operatives still alive on the same map as the shuttle,
        // or on the same map as the station.
        // If there are, the round can continue.
        var operatives = EntityQuery<NukeOperativeComponent, MobStateComponent, TransformComponent>(true);
        var operativesAlive = operatives
            .Where(op =>
                op.Item3.MapID == shuttleMapId
                || op.Item3.MapID == targetStationMap)
            .Any(op => op.Item2.CurrentState == MobState.Alive && op.Item1.Running);

        if (operativesAlive)
            return; // There are living operatives than can access the shuttle, or are still on the station's map.

        // Check that there are spawns available and that they can access the shuttle.
        var spawnsAvailable = EntityQuery<NukeOperativeSpawnerComponent>(true).Any();
        if (spawnsAvailable && CompOrNull<RuleGridsComponent>(ent)?.Map == shuttleMapId)
            return; // Ghost spawns can still access the shuttle. Continue the round.

        // The shuttle is inaccessible to both living nuke operatives and yet to spawn nuke operatives,
        // and there are no nuclear operatives on the target station's map.
        nukeops.WinConditions.Add(spawnsAvailable
            ? WinCondition.NukiesAbandoned
            : WinCondition.AllNukiesDead);

        SetWinType(ent, WinType.CrewMajor, false);

        if (nukeops.RoundEndBehavior == RoundEndBehavior.Nothing) // It's still worth checking if operatives have all died, even if the round-end behaviour is nothing.
            return; // Shouldn't actually try to end the round in the case of nothing though.

        _roundEndSystem.DoRoundEndBehavior(nukeops.RoundEndBehavior,
        nukeops.EvacShuttleTime,
        nukeops.RoundEndTextSender,
        nukeops.RoundEndTextShuttleCall,
        nukeops.RoundEndTextAnnouncement);


        // prevent it called multiple times
        nukeops.RoundEndBehavior = RoundEndBehavior.Nothing;
    }*/
}
