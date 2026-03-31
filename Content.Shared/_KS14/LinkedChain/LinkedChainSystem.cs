using Content.Shared._KS14.Chain;
using Content.Shared.Interaction;
using Content.Shared.Trigger;
using Content.Shared.Trigger.Systems;
using Content.Shared.Verbs;

namespace Content.Shared._KS14.LinkedChain;

/// <summary>
///     For linkable and triggerable chains.
/// </summary>
public sealed class LinkedChainSystem : EntitySystem
{
    [Dependency] private readonly TriggerSystem _triggerSystem = default!;
    [Dependency] private readonly ChainSystem _chainSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LinkedChainStartComponent, ChainSegmentedEvent>(OnStartAdjacentLinkBroken);
        SubscribeLocalEvent<LinkedChainStartComponent, GetVerbsEvent<AlternativeVerb>>(OnGetAltVerbs);

        SubscribeLocalEvent<LinkedChainStartComponent, ChainInitiallyBrokenEvent>(OnStartChainBroken);
        SubscribeLocalEvent<LinkedChainEndComponent, ChainInitiallyBrokenEvent>(OnEndChainBroken);

        SubscribeLocalEvent<LinkedChainStartComponent, TriggerEvent>(OnStartTriggered);
        SubscribeLocalEvent<LinkedChainEndComponent, InteractUsingEvent>(OnEndInteracted);

        SubscribeLocalEvent<LinkedChainStartComponent, ComponentShutdown>(OnStartShutdown);
        SubscribeLocalEvent<LinkedChainEndComponent, ComponentShutdown>(OnEndShutdown);
    }

    private void OnStartAdjacentLinkBroken(Entity<LinkedChainStartComponent> entity, ref ChainSegmentedEvent args)
    {
        _chainSystem.TryBreakChainFrom(entity.Owner, removeJoints: true);
    }

    private void OnStartChainBroken(Entity<LinkedChainStartComponent> entity, ref ChainInitiallyBrokenEvent args)
    {
        entity.Comp.EndUid = null;
        Dirty(entity);
    }

    private void OnEndChainBroken(Entity<LinkedChainEndComponent> entity, ref ChainInitiallyBrokenEvent args)
    {
        entity.Comp.StartUid = null;
        Dirty(entity);
    }

    private void OnGetAltVerbs(Entity<LinkedChainStartComponent> entity, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess || !args.CanComplexInteract ||
            !_chainSystem.HasAdjacentLink(entity.Owner))
            return;

        var verb = new AlternativeVerb()
        {
            Act = () => { _chainSystem.TryBreakChainFrom(entity.Owner, removeJoints: true); },
            Text = Loc.GetString("linkedchain-verb-separate"),
            Priority = 4
        };

        args.Verbs.Add(verb);
    }

    private void OnStartTriggered(Entity<LinkedChainStartComponent> entity, ref TriggerEvent args)
    {
        if (entity.Comp.EndUid is not { } endUid)
            return;

        if (args.Handled ||
            (args.Key is { } key && !entity.Comp.KeysIn.Contains(args.Key))) // because a null key triggers everything
            return;

        if (!_chainSystem.IsChainFullyIntact(entity.Owner))
            return;

        // Propagate the trigger
        _triggerSystem.Trigger(endUid, args.User, args.Key, predicted: false);
    }

    private void OnEndInteracted(Entity<LinkedChainEndComponent> entity, ref InteractUsingEvent args)
    {
        if (entity.Comp.StartUid is { } ||
            !TryComp<LinkedChainStartComponent>(args.Used, out var startComponent))
            return;

        if (!_chainSystem.TrySpawnChainInbetween(
            startComponent.SegmentId,
            Transform(entity).Coordinates,
            startComponent.Segments,
            startComponent.SegmentOffset,
            args.Used,
            entity.Owner,
            out var _
        ))
            return;

        entity.Comp.StartUid = args.Used;
        Dirty(entity);

        startComponent.EndUid = entity.Owner;
        Dirty(args.Used, startComponent);
    }

    private void OnStartShutdown(Entity<LinkedChainStartComponent> entity, ref ComponentShutdown args)
    {
        if (!TryComp<LinkedChainEndComponent>(entity.Comp.EndUid, out var endComponent))
            return;

        endComponent.StartUid = null;
        Dirty(entity.Comp.EndUid.Value, endComponent);
    }

    private void OnEndShutdown(Entity<LinkedChainEndComponent> entity, ref ComponentShutdown args)
    {
        if (!TryComp<LinkedChainStartComponent>(entity.Comp.StartUid, out var startComponent))
            return;

        startComponent.EndUid = null;
        Dirty(entity.Comp.StartUid.Value, startComponent);
    }
}
