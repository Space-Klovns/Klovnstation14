using Content.Shared._KS14.Dissolve;
using Robust.Shared.Containers;
using Robust.Shared.Spawners;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Dissolve;

/// <summary>
///     Dissolves things away instead of deleting them: the clients show the dissolve, and the entity is deleted just
///         after it has finished.
/// </summary>
public sealed partial class KsDissolveSystem : EntitySystem
{
    /// <summary>
    ///     How long the entity lingers after the dissolve, so that a client's own clock, which runs a little behind,
    ///         has finished it before the entity is gone.
    /// </summary>
    public static readonly TimeSpan Linger = TimeSpan.FromSeconds(0.5);

    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private EntityQuery<KsDissolveComponent> _dissolveQuery = default!;

    /// <summary>
    ///     Starts dissolving an entity, and deletes it afterwards. Does nothing if it is already dissolving; one
    ///         materialising turns round.
    /// </summary>
    /// <param name="uid">The entity.</param>
    /// <param name="duration">How long the dissolve takes, or the component's default.</param>
    public void Dissolve(EntityUid uid, TimeSpan? duration = null)
    {
        if (TerminatingOrDeleted(uid))
            return;

        // One still materialising turns round and dissolves from now.
        if (!_dissolveQuery.TryComp(uid, out var dissolveComponent))
            dissolveComponent = AddComp<KsDissolveComponent>(uid);
        else if (dissolveComponent.Reverse)
            dissolveComponent.Reverse = false;
        else
            return;

        dissolveComponent.StartTime = _gameTiming.CurTime;
        if (duration is { } customDuration)
            dissolveComponent.Duration = customDuration;
        Dirty(uid, dissolveComponent);

        var lifetime = (float)(dissolveComponent.Duration + Linger).TotalSeconds;
        // One that was already counting down to its own end keeps it, if that comes first.
        if (TryComp<TimedDespawnComponent>(uid, out var existingDespawn))
        {
            existingDespawn.Lifetime = Math.Min(existingDespawn.Lifetime, lifetime);
        }
        else
        {
            AddComp(uid, new TimedDespawnComponent { Lifetime = lifetime });
            dissolveComponent.OwnsDespawn = true;
        }
    }

    /// <summary>
    ///     Makes an entity materialise: the dissolve played backwards, from nothing to whole. The entity is kept and
    ///         the component taken off once it has finished. Does nothing if it is already dissolving away; restarts
    ///         one already materialising (on the server's clock).
    /// </summary>
    /// <param name="uid">The entity, usually just spawned.</param>
    /// <param name="duration">How long it takes, or the component's default.</param>
    public void Materialise(EntityUid uid, TimeSpan? duration = null)
    {
        if (TerminatingOrDeleted(uid))
            return;

        // Already materialising: it carries on, from now.
        if (!_dissolveQuery.TryComp(uid, out var dissolveComponent))
            dissolveComponent = AddComp<KsDissolveComponent>(uid);
        else if (!dissolveComponent.Reverse)
            return;

        dissolveComponent.Reverse = true;
        dissolveComponent.StartTime = _gameTiming.CurTime;
        if (duration is { } customDuration)
            dissolveComponent.Duration = customDuration;
        Dirty(uid, dissolveComponent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // A finished materialise leaves the entity whole: the component goes, a moment late as for a dissolve.
        var curTime = _gameTiming.CurTime;
        var query = EntityQueryEnumerator<KsDissolveComponent>();
        while (query.MoveNext(out var uid, out var dissolveComponent))
        {
            if (dissolveComponent.Reverse &&
                dissolveComponent.LifeStage <= ComponentLifeStage.Running &&
                curTime >= dissolveComponent.StartTime + dissolveComponent.Duration + Linger)
                RemCompDeferred<KsDissolveComponent>(uid);
        }
    }

    /// <summary>
    ///     Whether the entity is materialising, not yet whole.
    /// </summary>
    public bool IsMaterialising(EntityUid uid)
        => _dissolveQuery.TryComp(uid, out var dissolveComponent) && dissolveComponent.Reverse;

    /// <summary>
    ///     Calls a dissolve off: the entity stays, and the clients draw it whole again as the component goes. Does
    ///         nothing if it isn't dissolving.
    /// </summary>
    public void Cancel(Entity<KsDissolveComponent?> entity)
    {
        if (!_dissolveQuery.Resolve(entity.Owner, ref entity.Comp, logMissing: false) ||
            entity.Comp.LifeStage > ComponentLifeStage.Running)
            return;

        // Only the timer the dissolve started: one the entity already had is its own business.
        if (entity.Comp.OwnsDespawn)
            RemCompDeferred<TimedDespawnComponent>(entity.Owner);

        RemCompDeferred<KsDissolveComponent>(entity.Owner);
    }

    /// <summary>
    ///     Picked up, or put in something, while it dissolves: someone wants it after all, and it must not vanish from
    ///         their hands a moment later.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnInsertedIntoContainer(Entity<KsDissolveComponent> entity, ref EntGotInsertedIntoContainerMessage args)
    {
        Cancel(entity.AsNullable());
    }

    /// <summary>
    ///     Whether the entity is dissolving away, to be deleted.
    /// </summary>
    public bool IsDissolving(EntityUid uid)
        => _dissolveQuery.TryComp(uid, out var dissolveComponent) && !dissolveComponent.Reverse;
}
