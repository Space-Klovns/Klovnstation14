using Content.Shared._KS14.PopupLocalization;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Popups;
using Robust.Shared.Map;

namespace Content.Shared._KS14.Ladders;

/// <summary>
///     Links ladders and moves whoever climbs one to the other end.
/// </summary>
/// <remarks>
///     TODO: climb up/down animations like goonstation's (the user shrinking into the hatch, or rising up the wall).
/// </remarks>
public sealed partial class KsLadderSystem : EntitySystem
{
    [Dependency] private SharedDoAfterSystem _doAfterSystem = default!;
    [Dependency] private PullingSystem _pullingSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private EntityQuery<KsLadderComponent> _ladderQuery = default!;

    private const string NotLinkedMessage = "ks-ladder-not-linked";

    /// <summary>
    ///     Links two ladders to each other, unlinking whatever either was linked to before.
    /// </summary>
    public void Link(Entity<KsLadderComponent?> first, Entity<KsLadderComponent?> second)
    {
        if (first.Owner == second.Owner
            || !_ladderQuery.Resolve(first.Owner, ref first.Comp)
            || !_ladderQuery.Resolve(second.Owner, ref second.Comp))
            return;

        Unlink((first.Owner, first.Comp));
        Unlink((second.Owner, second.Comp));

        first.Comp.Linked = second.Owner;
        second.Comp.Linked = first.Owner;
        Dirty(first.Owner, first.Comp);
        Dirty(second.Owner, second.Comp);
    }

    /// <summary>
    ///     Unlinks a ladder, and the ladder at its other end if that one points back.
    /// </summary>
    public void Unlink(Entity<KsLadderComponent> ladder)
    {
        if (ladder.Comp.Linked is not { } linkedUid)
            return;

        ladder.Comp.Linked = null;
        Dirty(ladder);

        if (_ladderQuery.TryComp(linkedUid, out var linkedComponent) && linkedComponent.Linked == ladder.Owner)
        {
            linkedComponent.Linked = null;
            Dirty(linkedUid, linkedComponent);
        }
    }

    [SubscribeLocalEvent]
    private void OnMapInit(Entity<KsLadderComponent> entity, ref MapInitEvent args)
    {
        if (entity.Comp.Linked != null || entity.Comp.LinkKey is not { } key)
            return;

        // All, not just unpaused: the other end may be on a map that is still paused.
        var query = AllEntityQuery<KsLadderComponent>();
        while (query.MoveNext(out var otherUid, out var otherComponent))
        {
            if (otherUid == entity.Owner || otherComponent.Linked != null || otherComponent.LinkKey != key)
                continue;

            Link(entity.AsNullable(), (otherUid, otherComponent));
            return;
        }
    }

    [SubscribeLocalEvent]
    private void OnShutdown(Entity<KsLadderComponent> entity, ref ComponentShutdown args)
    {
        Unlink(entity);
    }

    [SubscribeLocalEvent]
    private void OnActivate(Entity<KsLadderComponent> entity, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex)
            return;

        args.Handled = true;
        if (!TryGetDestination(entity, out _))
        {
            _popupSystem.PopupEntity(KsPopupMessage.Create(NotLinkedMessage), entity, args.User);
            return;
        }

        if (entity.Comp.UseDelay is not { } delay)
        {
            Climb(entity, args.User);
            return;
        }

        var doAfter = new DoAfterArgs(EntityManager, args.User, delay, new KsLadderClimbDoAfterEvent(), entity, target: entity)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = false,
        };
        _doAfterSystem.TryStartDoAfter(doAfter);
    }

    [SubscribeLocalEvent]
    private void OnClimbDoAfter(Entity<KsLadderComponent> entity, ref KsLadderClimbDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled)
            return;

        args.Handled = true;
        Climb(entity, args.User);
    }

    /// <summary>
    ///     Where a climb up or down this ladder ends: the linked ladder's position, if that is still a ladder in the world.
    /// </summary>
    public bool TryGetDestination(Entity<KsLadderComponent> ladder, out EntityCoordinates destination)
    {
        destination = default;
        if (ladder.Comp.Linked is not { } linkedUid
            || TerminatingOrDeleted(linkedUid)
            || !_ladderQuery.HasComp(linkedUid))
            return false;

        var linkedTransform = Transform(linkedUid);
        if (linkedTransform.MapID == MapId.Nullspace)
            return false;

        destination = linkedTransform.Coordinates;
        return _transformSystem.IsValid(destination);
    }

    private void Climb(Entity<KsLadderComponent> ladder, EntityUid user)
    {
        if (!TryGetDestination(ladder, out var destination))
        {
            _popupSystem.PopupEntity(KsPopupMessage.Create(NotLinkedMessage), ladder, user);
            return;
        }

        // Nobody drags the climber along; whoever is being pulled comes too.
        if (TryComp<PullableComponent>(user, out var userPullable) && userPullable.BeingPulled)
            _pullingSystem.TryStopPull(user, userPullable);

        EntityUid? pulledUid = null;
        if (TryComp<PullerComponent>(user, out var pullerComponent)
            && pullerComponent.Pulling is { } pulling
            && TryComp<PullableComponent>(pulling, out var pulledPullable))
        {
            _pullingSystem.TryStopPull(pulling, pulledPullable);
            pulledUid = pulling;
        }

        _transformSystem.SetCoordinates(user, destination);
        if (pulledUid is not { } pulled)
            return;

        _transformSystem.SetCoordinates(pulled, destination);
        _pullingSystem.TryStartPull(user, pulled);
    }
}
