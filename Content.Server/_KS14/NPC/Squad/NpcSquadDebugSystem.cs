using Content.Server._KS14.NPC.Meters;
using Content.Server._KS14.NPC.Perception;
using Content.Server._KS14.NPC.Squad.Tactics;
using Content.Shared._KS14.NPC;
using Content.Shared.GameTicking;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._KS14.NPC.Squad;

/// <summary>
///     Sends a snapshot of every NPC squad to players who toggled it with
///         <see cref="Content.Server._KS14.NPC.Commands.SquadDebugCommand"/>. Does nothing while nobody is watching.
/// </summary>
public sealed partial class NpcSquadDebugSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private NpcMeterSystem _npcMeterSystem = default!;
    [Dependency] private KillZones.NpcKillZoneSystem _npcKillZoneSystem = default!;
    [Dependency] private NpcPerceptionSystem _npcPerceptionSystem = default!;
    [Dependency] private NpcSquadTacticsSystem _npcSquadTacticsSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    [Dependency] private EntityQuery<NpcPerceptionComponent> _perceptionQuery = default!;
    [Dependency] private EntityQuery<NpcMetersComponent> _metersQuery = default!;

    private static readonly TimeSpan SendInterval = TimeSpan.FromSeconds(0.5);

    private readonly HashSet<ICommonSession> _debuggingSessions = new();
    private TimeSpan _nextSend;

    public override void Initialize()
    {
        base.Initialize();
        _playerManager.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _playerManager.PlayerStatusChanged -= OnPlayerStatusChanged;
        _debuggingSessions.Clear();
    }

    /// <summary>
    ///     Toggles squad debugging for <paramref name="session"/>, returning whether it is now on.
    /// </summary>
    public bool Toggle(ICommonSession session)
    {
        var enabled = _debuggingSessions.Add(session);
        if (!enabled)
            _debuggingSessions.Remove(session);

        RaiseNetworkEvent(new SquadDebugStateMessage { Enabled = enabled }, session.Channel);
        return enabled;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_debuggingSessions.Count == 0)
            return;

        var now = _gameTiming.CurTime;
        if (now < _nextSend)
            return;

        _nextSend = now + SendInterval;

        var message = new SquadDebugDataMessage();
        var squadEnumerator = EntityQueryEnumerator<NpcSquadComponent>();

        while (squadEnumerator.MoveNext(out var squadUid, out var squadComponent))
        {
            if (squadComponent.Members.Count == 0)
                continue;

            var squad = new SquadDebugSquad
            {
                Squad = GetNetEntity(squadUid),
                Threat = squadComponent.ThreatCoordinates is { } threatCoordinates ? GetNetCoordinates(threatCoordinates) : null,
            };

            foreach (var memberUid in squadComponent.Members)
            {
                var memberNetCoordinates = GetNetCoordinates(Transform(memberUid).Coordinates);

                if (memberUid == squadComponent.Leader)
                    squad.Leader = memberNetCoordinates;
                else
                    squad.Members.Add(memberNetCoordinates);

                if (squadComponent.CoverPlan is { HasRoom: true } plan &&
                    plan.Assignments.TryGetValue(memberUid, out var assignment))
                    squad.Assignments.Add(new SquadDebugAssignment(memberNetCoordinates, GetNetCoordinates(assignment.Coordinates)));

                AddContacts(squad, memberUid, memberNetCoordinates);
                AddOrder(squad, memberUid, memberNetCoordinates);
                AddMeters(squad, memberUid, memberNetCoordinates);
            }

            AddHunt(squad, squadUid);

            if (squadComponent.CoverPlan is { HasRoom: true } coverPlan)
            {
                foreach (var threshold in coverPlan.Thresholds)
                {
                    squad.Thresholds.Add(GetNetCoordinates(new EntityCoordinates(coverPlan.GridUid, threshold.Center)));
                }
            }

            message.Squads.Add(squad);
        }

        // NPCs on their own hunt as squads of one: draw those that are up to something, or have a meter worth
        //      seeing, as one.
        var lonerEnumerator = EntityQueryEnumerator<NpcSquadMemberComponent>();
        while (lonerEnumerator.MoveNext(out var uid, out var squadMemberComponent))
        {
            if (squadMemberComponent.Squad != null ||
                !_npcSquadTacticsSystem.TryGetOrder(uid, out _) && !_metersQuery.HasComp(uid))
                continue;

            var netCoordinates = GetNetCoordinates(Transform(uid).Coordinates);
            var loner = new SquadDebugSquad
            {
                Squad = GetNetEntity(uid),
                Leader = netCoordinates,
            };

            AddContacts(loner, uid, netCoordinates);
            AddOrder(loner, uid, netCoordinates);
            AddMeters(loner, uid, netCoordinates);
            AddHunt(loner, uid);
            message.Squads.Add(loner);
        }

        AddKillZones(message);

        foreach (var session in _debuggingSessions)
        {
            RaiseNetworkEvent(message, session.Channel);
        }
    }

    private readonly List<(Vector2i Center, IReadOnlyDictionary<Vector2i, float> Tiles, float SecondsLeft)> _killZones = new();

    private void AddKillZones(SquadDebugDataMessage message)
    {
        var enumerator = EntityQueryEnumerator<KillZones.NpcKillZonesComponent>();
        while (enumerator.MoveNext(out var gridUid, out _))
        {
            _killZones.Clear();
            _npcKillZoneSystem.GetZones(gridUid, _killZones);

            foreach (var (center, tiles, secondsLeft) in _killZones)
            {
                var zone = new SquadDebugKillZone
                {
                    Grid = GetNetEntity(gridUid),
                    Center = center,
                    SecondsLeft = secondsLeft,
                };

                foreach (var (tile, danger) in tiles)
                {
                    zone.Tiles.Add(tile);
                    zone.Danger.Add(danger);
                }

                message.KillZones.Add(zone);
            }
        }
    }

    private void AddContacts(SquadDebugSquad squad, EntityUid memberUid, NetCoordinates memberNetCoordinates)
    {
        if (!_perceptionQuery.TryComp(memberUid, out var perceptionComponent))
            return;

        foreach (var targetUid in perceptionComponent.Contacts.Keys)
        {
            if (!_npcPerceptionSystem.TryGetBelievedCoordinates((memberUid, perceptionComponent), targetUid, out var believedCoordinates, out var state, out _) ||
                TerminatingOrDeleted(believedCoordinates.EntityId))
                continue;

            NetCoordinates? predicted = state == NpcContactState.Lost &&
                _npcPerceptionSystem.TryGetPredictedCoordinates((memberUid, perceptionComponent), targetUid, out var predictedCoordinates)
                    ? GetNetCoordinates(predictedCoordinates)
                    : null;

            // Relative to the grid or map: coordinates relative to the hostile would name an entity the client may
            //      not have, such as one in a locker.
            squad.Contacts.Add(new SquadDebugContact(memberNetCoordinates,
                GetNetCoordinates(_transformSystem.GetMoverCoordinates(believedCoordinates)),
                state.Value,
                predicted));
        }
    }

    private void AddOrder(SquadDebugSquad squad, EntityUid memberUid, NetCoordinates memberNetCoordinates)
    {
        if (!_npcSquadTacticsSystem.TryGetOrder(memberUid, out var order) || TerminatingOrDeleted(order.Coordinates.EntityId))
            return;

        squad.Orders.Add(new SquadDebugOrder(memberNetCoordinates, GetNetCoordinates(_transformSystem.GetMoverCoordinates(order.Coordinates)), order.Kind));
    }

    private readonly List<(Robust.Shared.Prototypes.ProtoId<NpcMeterPrototype> MeterId, float Value, float Max)> _meterReadings = new();

    private void AddMeters(SquadDebugSquad squad, EntityUid memberUid, NetCoordinates memberNetCoordinates)
    {
        _meterReadings.Clear();
        _npcMeterSystem.GetAll(memberUid, _meterReadings);

        foreach (var (meterId, value, max) in _meterReadings)
        {
            squad.Meters.Add(new SquadDebugMeter(memberNetCoordinates, meterId, value, max));
        }
    }

    private void AddHunt(SquadDebugSquad squad, EntityUid issuerUid)
    {
        if (_npcSquadTacticsSystem.GetHunt(issuerUid) is not { } hunt)
            return;

        squad.HuntPhase = hunt.Phase;

        if (!TerminatingOrDeleted(hunt.PredictedCoordinates.EntityId))
            squad.HuntPredicted = GetNetCoordinates(hunt.PredictedCoordinates);

        foreach (var entrance in hunt.Entrances)
        {
            if (!TerminatingOrDeleted(entrance.StageCoordinates.EntityId))
                squad.HuntEntrances.Add(GetNetCoordinates(entrance.StageCoordinates));
        }

        foreach (var point in hunt.SearchPoints)
        {
            if (!TerminatingOrDeleted(point.Coordinates.EntityId))
                squad.SearchPoints.Add(new SquadDebugSearchPoint(GetNetCoordinates(_transformSystem.GetMoverCoordinates(point.Coordinates)), point.Cleared, point.StorageUid != null));
        }
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs e)
    {
        if (e.NewStatus == SessionStatus.Disconnected)
            _debuggingSessions.Remove(e.Session);
    }

    /// <summary>
    ///     Sessions survive a round restart, so tell each client to drop its overlay rather than leave it
    ///         showing the last round's squads.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        foreach (var session in _debuggingSessions)
        {
            RaiseNetworkEvent(new SquadDebugStateMessage { Enabled = false }, session.Channel);
        }

        _debuggingSessions.Clear();
    }
}
