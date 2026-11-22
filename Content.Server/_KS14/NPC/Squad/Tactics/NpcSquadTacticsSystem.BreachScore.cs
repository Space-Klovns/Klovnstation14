using System.Numerics;
using Content.Shared._KS14.NPC;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._KS14.NPC.Squad.Tactics;

/// <summary>
///     Which wall makes the best way in, for squads that are there to breach (<see cref="NpcSquadTacticsSettings.BreachScoring"/>).
///         Everyone else takes the nearest walk, as <see cref="FindWallCandidates"/> sorts them.
/// </summary>
public sealed partial class NpcSquadTacticsSystem
{
    /// <summary>
    ///     How far past the spot in front of a wall the charge is set off from, straight back, for the sight check: within
    ///         <see cref="MinStandOff"/> and <see cref="MaxStandOff"/> of the charge, as <see cref="TryFindStandOff"/> looks.
    /// </summary>
    private const int BreachStandOffSteps = 3;

    /// <summary>
    ///     How far a hostile is taken to see, for the sight check.
    /// </summary>
    private const float BreachSightRange = 30f;

    /// <summary>
    ///     How many charges each grid remembers.
    /// </summary>
    private const int MaxRememberedBreaches = 32;

    private static readonly List<NpcContactState> AnyContactState = [NpcContactState.Visible, NpcContactState.Lost, NpcContactState.Concealed, NpcContactState.Suspected, NpcContactState.Reported];

    private readonly List<(float Score, int Index)> _breachScores = new();
    private readonly List<(int Steps, Vector2i Room, Vector2i Wall, Vector2i Outside)> _breachScratch = new();
    private readonly List<Vector2i> _breachOpenings = new();
    private readonly List<MapCoordinates> _breachWatchers = new();
    private readonly HashSet<EntityUid> _breachContacts = new();

    /// <summary>
    ///     Puts <see cref="_wallCandidates"/> best first, lowest score first: the far side's distance from where the hostile
    ///         is believed to be (<see cref="NpcHunt.PredictedCoordinates"/>), the walk there, and penalties for a way in or a
    ///         recent hole nearby and for a spot to set the charge off from that a known hostile can see. Ties keep the walk's
    ///         order.
    /// </summary>
    private void OrderByBreachScore(NpcSquadPlan plan, Entity<MapGridComponent> grid, NpcSquadTacticsSettings settings)
    {
        if (_wallCandidates.Count < 2)
            return;

        var hunt = plan.Hunt!;
        Vector2? targetTile = _transformSystem.GetGrid(hunt.PredictedCoordinates) == grid.Owner
            ? (Vector2) _mapSystem.TileIndicesFor(grid, hunt.PredictedCoordinates)
            : null;

        GatherOpenings(hunt, grid);
        GatherWatchers(plan, grid);

        var now = _gameTiming.CurTime;
        TryComp<NpcBreachMemoryComponent>(grid.Owner, out var memoryComponent);
        var range = settings.BreachOpeningRange;

        _breachScores.Clear();
        for (var i = 0; i < _wallCandidates.Count; i++)
        {
            var (steps, roomTile, wallTile, outsideTile) = _wallCandidates[i];
            var score = settings.BreachWalkWeight * steps;

            if (targetTile is { } target)
                score += settings.BreachTargetWeight * ((Vector2) roomTile - target).Length();

            if (IsNearAny(_breachOpenings, wallTile, range))
                score += settings.BreachOpeningPenalty;

            if (memoryComponent != null && IsNearRecentBreach(memoryComponent, wallTile, range, now - settings.BreachMemoryTime))
                score += settings.BreachRecentPenalty;

            _breachScores.Add((score, i));
        }

        // Every one gets its line of sight, not only the best few: TryPickWallCandidate goes down the list past any that turn out
        //      unsafe to blow or out of reach, however many, and one left unchecked there would come before every one checked and
        //      seen. There are only as many as the room has walls within a walk, and this runs once per charge picked.
        if (_breachWatchers.Count > 0 && settings.BreachSightPenalty > 0f)
        {
            for (var i = 0; i < _breachScores.Count; i++)
            {
                var (score, index) = _breachScores[i];
                var (_, _, wallTile, outsideTile) = _wallCandidates[index];
                var back = outsideTile - wallTile;
                var standOffTile = outsideTile + back * BreachStandOffSteps;
                var standOff = _transformSystem.ToMapCoordinates(GetTileCoordinates(grid.Owner, standOffTile));

                foreach (var watcher in _breachWatchers)
                {
                    if (!_npcLineOfSightSystem.InLineOfSight(watcher, standOff, BreachSightRange))
                        continue;

                    _breachScores[i] = (score + settings.BreachSightPenalty, index);
                    break;
                }
            }
        }

        _breachScores.Sort(CompareBreachScores);

        _breachScratch.Clear();
        _breachScratch.AddRange(_wallCandidates);
        _wallCandidates.Clear();
        foreach (var (_, index) in _breachScores)
            _wallCandidates.Add(_breachScratch[index]);
    }

    private static int CompareBreachScores((float Score, int Index) a, (float Score, int Index) b)
    {
        var byScore = a.Score.CompareTo(b.Score);
        return byScore != 0 ? byScore : a.Index.CompareTo(b.Index);
    }

    /// <summary>
    ///     The ways into the hostile's room there are already: its doorways and where its entrances lead in.
    /// </summary>
    private void GatherOpenings(NpcHunt hunt, Entity<MapGridComponent> grid)
    {
        _breachOpenings.Clear();
        _breachOpenings.AddRange(hunt.ThresholdTiles);

        foreach (var entrance in hunt.Entrances)
        {
            if (_transformSystem.GetGrid(entrance.EntryCoordinates) == grid.Owner)
                _breachOpenings.Add(_mapSystem.TileIndicesFor(grid, entrance.EntryCoordinates));
        }
    }

    /// <summary>
    ///     Where hostiles are believed to be, from everything the members know of, and where the hunt thinks its own one went.
    /// </summary>
    private void GatherWatchers(NpcSquadPlan plan, Entity<MapGridComponent> grid)
    {
        _breachWatchers.Clear();

        var hunt = plan.Hunt!;
        if (_transformSystem.GetGrid(hunt.PredictedCoordinates) == grid.Owner)
            _breachWatchers.Add(_transformSystem.ToMapCoordinates(hunt.PredictedCoordinates));

        _breachContacts.Clear();
        foreach (var memberUid in _members)
            _npcPerceptionSystem.GetContacts(memberUid, AnyContactState, reactedOnly: false, _breachContacts);

        foreach (var contactUid in _breachContacts)
        {
            if (hunt.TargetUid == contactUid)
                continue;

            foreach (var memberUid in _members)
            {
                if (!_npcPerceptionSystem.TryGetContact(memberUid, contactUid, out var contact) ||
                    _transformSystem.GetGrid(contact.LastKnownCoordinates) != grid.Owner)
                    continue;

                _breachWatchers.Add(_transformSystem.ToMapCoordinates(contact.LastKnownCoordinates));
                break;
            }
        }
    }

    private static bool IsNearAny(List<Vector2i> tiles, Vector2i tile, float range)
    {
        var rangeSquared = range * range;
        foreach (var other in tiles)
        {
            if (((Vector2) (other - tile)).LengthSquared() <= rangeSquared)
                return true;
        }

        return false;
    }

    private static bool IsNearRecentBreach(NpcBreachMemoryComponent memoryComponent, Vector2i tile, float range, TimeSpan since)
    {
        var rangeSquared = range * range;
        foreach (var (breachTile, at) in memoryComponent.Breaches)
        {
            if (at >= since && ((Vector2) (breachTile - tile)).LengthSquared() <= rangeSquared)
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Keeps where a charge just went off on its grid's <see cref="NpcBreachMemoryComponent"/>, for every squad after.
    /// </summary>
    private void RememberBreach(NpcChargeSite site, TimeSpan now)
    {
        var gridUid = _transformSystem.GetGrid(site.StandCoordinates);
        if (gridUid is not { } validGridUid || TerminatingOrDeleted(validGridUid))
            return;

        RecordBreach(validGridUid, site.Tile, now);
    }

    /// <summary>
    ///     Remembers a charge going off on <paramref name="tile"/> of <paramref name="gridUid"/>, as if a squad had set it off.
    /// </summary>
    public void RecordBreach(EntityUid gridUid, Vector2i tile, TimeSpan at)
    {
        var memoryComponent = EnsureComp<NpcBreachMemoryComponent>(gridUid);
        memoryComponent.Breaches.Add((tile, at));

        if (memoryComponent.Breaches.Count > MaxRememberedBreaches)
            memoryComponent.Breaches.RemoveRange(0, memoryComponent.Breaches.Count - MaxRememberedBreaches);
    }
}
