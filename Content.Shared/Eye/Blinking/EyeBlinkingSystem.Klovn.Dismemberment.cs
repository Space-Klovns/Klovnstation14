using Content.Shared.Bed.Sleep;
using Content.Shared.Body;
using Content.Shared.Eye.Blinding.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared.Eye.Blinking;

/// <summary>
///     Keeps blinking eyes in step with the body they are attached to, since organs can be cut off and reattached.
///         Nothing here knows which organ carried the eyes away: the eyes organ is told it left or joined a body
///         whichever of its ancestors moved.
/// </summary>
public abstract partial class EyeBlinkingSystem
{
    [Dependency] private INetManager _netManager = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private MobStateSystem _mobStateSystem = default!;
    [Dependency] private EntityQuery<SleepingComponent> _sleepingQuery = default!;
    [Dependency] private EntityQuery<BlindableComponent> _blindableQuery = default!;

    [SubscribeLocalEvent]
    private void OnOrganGotInserted(Entity<EyeBlinkingComponent> ent, ref OrganGotInsertedEvent args)
    {
        // State application rebuilds the hierarchy on the client; the server's state already says what these are.
        if (!_gameTiming.ApplyingState)
            SyncWithHolder(ent, args.Target);

        OnEyesAttached(ent, args.Target);
    }

    [SubscribeLocalEvent]
    private void OnOrganGotRemoved(Entity<EyeBlinkingComponent> ent, ref OrganGotRemovedEvent args)
    {
        // Raised before the organ's hierarchy is cleared, so its Body still names the old holder: use Target.
        OnEyesDetached(ent, args.Target);

        if (_gameTiming.ApplyingState)
            return;

        RemoveEyeAction(ent, args.Target);

        var previousStatus = ent.Comp.Status;
        if (previousStatus == BlinkStatus.Normal)
            return;

        // Eyes with no holder are neither asleep, dead nor blind; the next holder decides that.
        ent.Comp.Status = BlinkStatus.Normal;
        StatusChanged(ent, previousStatus);
        Dirty(ent);

        // A holder left with its eyes shut by these eyes would stay blind without them.
        if ((previousStatus & BlinkStatus.EyesClosed) != BlinkStatus.Normal)
            _blindable.UpdateIsBlind(args.Target);
    }

    /// <summary>
    ///     Called when these eyes join a holder's body, including during client state application.
    /// </summary>
    protected virtual void OnEyesAttached(Entity<EyeBlinkingComponent> ent, EntityUid holderUid) { }

    /// <summary>
    ///     Called when these eyes leave a holder's body, before the organ's own body reference is cleared.
    /// </summary>
    protected virtual void OnEyesDetached(Entity<EyeBlinkingComponent> ent, EntityUid holderUid) { }

    /// <summary>
    ///     Takes the status of a new holder, since the events that set it were raised while the eyes were elsewhere.
    /// </summary>
    private void SyncWithHolder(Entity<EyeBlinkingComponent> ent, EntityUid holderUid)
    {
        var previousStatus = ent.Comp.Status;
        var status = BlinkStatus.Normal;

        if (_sleepingQuery.HasComp(holderUid))
            status |= BlinkStatus.Sleeping;

        var dead = _mobStateSystem.IsDead(holderUid);
        if (dead)
            status |= BlinkStatus.Dead;

        ent.Comp.Status = status;

        // The holder may have been blind for the lack of these eyes; ask again now they are in.
        _blindable.UpdateIsBlind(holderUid);
        if (_blindableQuery.TryComp(holderUid, out var blindableComponent) && blindableComponent.IsBlind)
            ent.Comp.Status |= BlinkStatus.Blind;

        if (ent.Comp.Status != previousStatus)
            StatusChanged(ent, previousStatus);

        Dirty(ent);

        if (!dead)
            AddEyeAction(ent, holderUid);
    }

    /// <summary>
    ///     Takes the eye-toggling action off a holder these eyes are leaving, and deletes it: the next holder is given
    ///         a new one, as on revival.
    /// </summary>
    private void RemoveEyeAction(Entity<EyeBlinkingComponent> ent, EntityUid holderUid)
    {
        if (ent.Comp.EyeToggleActionEntity is not { } actionUid)
            return;

        _actions.RemoveAction(holderUid, actionUid);
        ent.Comp.EyeToggleActionEntity = null;
        Dirty(ent);

        // The action is only ever spawned by the server (ActionContainerSystem.EnsureAction).
        if (_netManager.IsServer)
            QueueDel(actionUid);
    }
}
