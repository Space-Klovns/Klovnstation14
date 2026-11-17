using Content.Server.GameTicking;
using Content.Server.Spawners.Components;
using Content.Server.Station.Systems;
using Robust.Shared.Map;
using Robust.Shared.Random;
using Robust.Shared.Log;

namespace Content.Server.Spawners.EntitySystems;

public sealed partial class SpawnPointSystem : EntitySystem
{
    [Dependency] private GameTicker _gameTicker = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private StationSystem _stationSystem = default!;
    [Dependency] private StationSpawningSystem _stationSpawning = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<PlayerSpawningEvent>(OnPlayerSpawning);
    }

    private void OnPlayerSpawning(PlayerSpawningEvent args)
    {
        if (args.SpawnResult != null)
            return;

        // TODO: Cache all this if it ends up important.
        // KS14 start: prefer the joining station's own spawn points, but fall back to every spawn point when it has none,
        //      as in scenarios, whose spawn points are not on a station
        var possiblePositions = CollectPossiblePositions(args, requireStation: true);
        if (possiblePositions.Count == 0 && args.Station != null)
            possiblePositions = CollectPossiblePositions(args, requireStation: false);
        // KS14 end

        if (possiblePositions.Count == 0)
        {
            // Ok we've still not returned, but we need to put them /somewhere/.
            // TODO: Refactor gameticker spawning code so we don't have to do this!
            var points2 = EntityQueryEnumerator<SpawnPointComponent, TransformComponent>();

            if (points2.MoveNext(out _, out var xform))
            {
                Log.Error($"Unable to pick a valid spawn point, picking random spawner as a backup.\nRunLevel: {_gameTicker.RunLevel} Station: {ToPrettyString(args.Station)} Job: {args.Job}");
                possiblePositions.Add(xform.Coordinates);
            }
            else
            {
                Log.Error($"No spawn points were available!\nRunLevel: {_gameTicker.RunLevel} Station: {ToPrettyString(args.Station)} Job: {args.Job}");
                return;
            }
        }

        var spawnLoc = _random.Pick(possiblePositions);

        args.SpawnResult = _stationSpawning.SpawnPlayerMob(
            spawnLoc,
            args.Job,
            args.HumanoidCharacterProfile,
            args.Station);
    }

    // KS14: moved out of OnPlayerSpawning so it can be run with and without the station check
    private List<EntityCoordinates> CollectPossiblePositions(PlayerSpawningEvent args, bool requireStation)
    {
        var points = EntityQueryEnumerator<SpawnPointComponent, TransformComponent>();
        var possiblePositions = new List<EntityCoordinates>();

        bool foundPicky = false; //KS14 - dont spawn on normal latejoins if youve found a picky latejoin

        while (points.MoveNext(out var uid, out var spawnPoint, out var xform))
        {
            if (requireStation /* KS14: added */ && args.Station != null && _stationSystem.GetOwningStation(uid, xform) != args.Station)
                continue;

            //KS14 start
            if (_gameTicker.RunLevel == GameRunLevel.InRound && spawnPoint.SpawnType == SpawnPointType.LateJoin && spawnPoint.PickyLatejoin == true &&
                (args.Job == null || spawnPoint.Job == null || spawnPoint.Job == args.Job))
            {
                if (foundPicky == false) //incredible demoncode to reset the found positions upon latejoin
                    possiblePositions = new List<EntityCoordinates>();
                possiblePositions.Add(xform.Coordinates);
                foundPicky = true;
            }

            if (_gameTicker.RunLevel == GameRunLevel.InRound && spawnPoint.SpawnType == SpawnPointType.LateJoin && spawnPoint.PickyLatejoin == false && foundPicky == false)
            {
                possiblePositions.Add(xform.Coordinates);
            }
            //KS14 end


            if (_gameTicker.RunLevel != GameRunLevel.InRound &&
                spawnPoint.SpawnType == SpawnPointType.Job &&
                (args.Job == null || spawnPoint.Job == null || spawnPoint.Job == args.Job))
            {
                possiblePositions.Add(xform.Coordinates);
            }
        }

        return possiblePositions;
    }
}
