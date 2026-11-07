using Content.Server._KS14.NPC.LineOfFire;
using Content.Shared._KS14.NPC;
using Content.Shared.GameTicking;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._KS14.NPC.Squad.Tactics;

/// <summary>
///     Tracks which players have <see cref="SquadPlanDebugCommand"/> on - for every squad, or one - and passes them what
///         <see cref="NpcSquadTacticsSystem"/> makes of a plan as it goes.
/// </summary>
/// <remarks>
///     Lazy, like the hunt debug: nothing here runs on its own. The tactics system asks <see cref="IsTracking"/> after
///         updating a plan, and a frame is only built if someone is watching that squad.
/// </remarks>
public sealed partial class NpcSquadPlanDebugSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private NpcLineOfFireSystem _npcLineOfFireSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    [Dependency] private EntityQuery<NpcOrderComponent> _orderQuery = default!;

    /// <summary>
    ///     Null value = every squad; otherwise a squad, or an NPC (meaning its squad, or itself).
    /// </summary>
    private readonly Dictionary<ICommonSession, EntityUid?> _debuggingSessions = new();

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
    ///     Toggles plan debugging for <paramref name="session"/>. The same scope again turns it off; anything else switches to
    ///         that scope. Returns the resulting state.
    /// </summary>
    public (bool Enabled, EntityUid? Target) Toggle(ICommonSession session, EntityUid? target)
    {
        if (_debuggingSessions.TryGetValue(session, out var currentTarget) && currentTarget == target)
        {
            _debuggingSessions.Remove(session);
            RaiseNetworkEvent(new SquadPlanDebugStateMessage { Enabled = false }, session.Channel);
            return (false, target);
        }

        _debuggingSessions[session] = target;
        RaiseNetworkEvent(new SquadPlanDebugStateMessage
        {
            Enabled = true,
            Target = target is { } uid ? GetNetEntity(uid) : null,
        }, session.Channel);
        return (true, target);
    }

    /// <summary>
    ///     Whether anyone would receive a frame for <paramref name="issuerUid"/>'s plan. Check before building one.
    /// </summary>
    public bool IsTracking(EntityUid issuerUid, List<EntityUid> memberUids)
    {
        foreach (var target in _debuggingSessions.Values)
        {
            if (Matches(target, issuerUid, memberUids))
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Sends where <paramref name="plan"/> stands to everyone watching it.
    /// </summary>
    public void SendFrame(EntityUid issuerUid, NpcSquadPlan plan, List<EntityUid> memberUids)
    {
        SquadPlanDebugDataMessage? message = null;

        foreach (var (session, target) in _debuggingSessions)
        {
            if (!Matches(target, issuerUid, memberUids))
                continue;

            message ??= BuildFrame(issuerUid, plan, memberUids);

            if (message != null)
                RaiseNetworkEvent(message, session.Channel);
        }
    }

    /// <summary>
    ///     Tells everyone watching that <paramref name="issuerUid"/> has no plan any more.
    /// </summary>
    public void SendEnded(EntityUid issuerUid, List<EntityUid> memberUids)
    {
        foreach (var (session, target) in _debuggingSessions)
        {
            if (Matches(target, issuerUid, memberUids))
                RaiseNetworkEvent(new SquadPlanDebugEndedMessage { Issuer = GetNetEntity(issuerUid) }, session.Channel);
        }
    }

    private static bool Matches(EntityUid? target, EntityUid issuerUid, List<EntityUid> memberUids)
    {
        return target is not { } targetUid || targetUid == issuerUid || memberUids.Contains(targetUid);
    }

    private SquadPlanDebugDataMessage? BuildFrame(EntityUid issuerUid, NpcSquadPlan plan, List<EntityUid> memberUids)
    {
        if (TerminatingOrDeleted(plan.TargetUid))
            return null;

        var now = _gameTiming.CurTime;
        var hunt = plan.Hunt;
        var settings = hunt?.Settings;

        var message = new SquadPlanDebugDataMessage
        {
            Issuer = GetNetEntity(issuerUid),
            State = plan.State,
            Reason = plan.Reason,
            Elapsed = (float) (now - plan.StartedAt).TotalSeconds,
            Timeout = settings == null ? 0f : (float) settings.PlanTimeout.TotalSeconds,
            Facts = plan.Facts,
            CurrentStep = plan.CurrentStep,
            Cost = plan.Cost,
            Target = GetNetCoordinates(Snapshot(Transform(plan.TargetUid).Coordinates)),
        };

        for (var i = 0; i < plan.StepCount; i++)
        {
            message.Steps.Add(plan.Steps[i]);
        }

        if (plan.Charge is { } charge)
        {
            message.ChargePhase = charge.Phase;
            message.ChargeStand = GetNetCoordinates(charge.Pick.Site.StandCoordinates);
            message.ChargeStandOff = GetNetCoordinates(charge.Pick.StandOffCoordinates);
        }

        foreach (var definition in NpcSquadGoap.Actions)
        {
            if ((plan.BannedActions & NpcSquadGoap.GetBan(definition.Action)) != 0x0)
                message.Banned.Add(definition.Action);
        }

        var targetMapCoordinates = _transformSystem.GetMapCoordinates(plan.TargetUid);

        foreach (var memberUid in memberUids)
        {
            var memberMapCoordinates = _transformSystem.GetMapCoordinates(memberUid);
            var memberCoordinates = GetNetCoordinates(Transform(memberUid).Coordinates);

            if (_npcLineOfFireSystem.TryGetFirstHit(memberMapCoordinates, targetMapCoordinates, _npcLineOfFireSystem.GetShotMask(memberUid), out var hit))
                message.Shots.Add(new SquadPlanDebugShot(memberCoordinates, GetNetCoordinates(Transform(hit.HitEntity).Coordinates), Clear: false));
            else
                message.Shots.Add(new SquadPlanDebugShot(memberCoordinates, message.Target, Clear: true));

            if (_orderQuery.TryComp(memberUid, out var orderComponent) &&
                orderComponent.Order is { } order &&
                !TerminatingOrDeleted(order.Coordinates.EntityId))
                message.Orders.Add(new SquadDebugOrder(memberCoordinates, GetNetCoordinates(Snapshot(order.Coordinates)), order.Kind));
        }

        foreach (var coordinates in plan.ProbePath)
        {
            if (!TerminatingOrDeleted(coordinates.EntityId))
                message.Path.Add(GetNetCoordinates(coordinates));
        }

        foreach (var barrierUid in plan.Breakers.Values)
        {
            if (!TerminatingOrDeleted(barrierUid))
                message.Barriers.Add(GetNetCoordinates(Snapshot(Transform(barrierUid).Coordinates)));
        }

        if (hunt != null)
        {
            foreach (var entrance in hunt.Entrances)
            {
                message.Entrances.Add(new HuntDebugEntrance(GetNetCoordinates(entrance.StageCoordinates), GetNetCoordinates(entrance.EntryCoordinates)));
            }
        }

        return message;
    }

    /// <summary>
    ///     Coordinates relative to the grid or map rather than to an entity the client may not have.
    /// </summary>
    private EntityCoordinates Snapshot(EntityCoordinates coordinates)
    {
        return TerminatingOrDeleted(coordinates.EntityId) ? coordinates : _transformSystem.GetMoverCoordinates(coordinates);
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs e)
    {
        if (e.NewStatus == SessionStatus.Disconnected)
            _debuggingSessions.Remove(e.Session);
    }

    /// <summary>
    ///     Sessions survive a round restart, so tell each client to drop its overlay rather than leave it showing the last
    ///         round's plans.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        foreach (var session in _debuggingSessions.Keys)
        {
            RaiseNetworkEvent(new SquadPlanDebugStateMessage { Enabled = false }, session.Channel);
        }

        _debuggingSessions.Clear();
    }
}
