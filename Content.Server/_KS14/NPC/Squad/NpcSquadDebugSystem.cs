using Content.Server._KS14.NPC.Perception;
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
    [Dependency] private NpcPerceptionSystem _npcPerceptionSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    [Dependency] private EntityQuery<NpcPerceptionComponent> _perceptionQuery = default!;

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
            }

            if (squadComponent.CoverPlan is { HasRoom: true } coverPlan)
            {
                foreach (var threshold in coverPlan.Thresholds)
                {
                    squad.Thresholds.Add(GetNetCoordinates(new EntityCoordinates(coverPlan.GridUid, threshold.Center)));
                }
            }

            message.Squads.Add(squad);
        }

        foreach (var session in _debuggingSessions)
        {
            RaiseNetworkEvent(message, session.Channel);
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
