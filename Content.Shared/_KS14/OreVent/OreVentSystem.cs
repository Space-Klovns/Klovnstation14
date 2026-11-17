using Content.Shared._KS14.PopupLocalization;
using Content.Shared._KS14.OreVent.Drone;
using Content.Shared._KS14.McqDialogue;
using Content.Shared._KS14.OreWell;
using Content.Shared._KS14.ScanDiscoverable.Base;
using Content.Shared.DoAfter;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Jittering;
using Content.Shared.Popups;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.OreVent;

public sealed partial class OreVentSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private INetManager _netManager = default!;
    [Dependency] private SharedDoAfterSystem _doAfterSystem = default!;
    [Dependency] private SharedJitteringSystem _jitteringSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private SharedAppearanceSystem _appearanceSystem = default!;
    [Dependency] private KsScanDiscoverableSystem _discoverableSystem = default!;
    [Dependency] private SharedOreVentDroneSystem _oreVentDroneSystem = default!;
    [Dependency] private OreWellSystem _oreWellSystem = default!;
    [Dependency] private McqDialogueSystem _mcqDialogueSystem = default!;
    [Dependency] private EntityQuery<McqDialogueHeldItemComponent> _dialogueHeldItemQuery = default!;

    private const string ExcavateYesId = "yes";
    private const string ExcavateNoId = "no";
    private static readonly LocId ExcavateYesLoc = "ks-specific-orevent-excavate-yes";
    private static readonly LocId ExcavateNoLoc = "ks-specific-orevent-excavate-no";

    public override void Initialize()
    {
        base.Initialize();
        InitialiseTapping();
    }

    [SubscribeLocalEvent]
    private void OnInteractUsing(Entity<OreVentComponent> entity, ref InteractUsingEvent args)
    {
        if (args.Handled ||
            entity.Comp.DoingClearing ||
            !_discoverableSystem.IsScanner(args.Used))
            return;

        if (entity.Comp.Tapped)
        {
            _popupSystem.PopupPredicted(
                KsPopupMessage.Create("ks-specific-orevent-alreadytapped"), entity, args.User, Filter.PvsExcept(args.User), true);

            return;
        }

        if (!_discoverableSystem.IsDiscovered(entity))
            return;

        if (entity.Comp.BeingTapped)
        {
            _popupSystem.PopupPredicted(
                KsPopupMessage.Create("ks-specific-orevent-whatareyoudoing"), entity, args.User, Filter.PvsExcept(args.User), true, type: PopupType.SmallCaution);

            return;
        }

        args.Handled = true;

        // tgstation asks before the wave defence starts. Predicted: the user's window opens at once. It closes if the
        //      scanner leaves their hand or they walk off.
        _mcqDialogueSystem.StartDialogue(entity.Owner, args.User,
            new[] { new McqDialogueData(ExcavateYesLoc, ExcavateYesId), new McqDialogueData(ExcavateNoLoc, ExcavateNoId) },
            settings: entity.Comp.ExcavateDialogue);
    }

    [SubscribeLocalEvent]
    private void OnExcavateDialogueSelected(Entity<OreVentComponent> entity, ref McqDialogueSelectedEvent args)
    {
        if (args.Id != ExcavateYesId ||
            !_dialogueHeldItemQuery.TryComp(args.Dialogue, out var heldItemComponent) ||
            heldItemComponent.HeldUid is not { } scannerUid)
            return;

        // The dialogue saw to the user still holding the scanner and being in reach, but the vent may have changed
        //      while it was open: someone else may have started it.
        if (entity.Comp.Tapped ||
            entity.Comp.BeingTapped ||
            entity.Comp.DoingClearing ||
            !_discoverableSystem.IsDiscovered(entity.Owner))
            return;

        var success = _doAfterSystem.TryStartDoAfter(new DoAfterArgs(EntityManager, args.Actor, entity.Comp.ClearingDuration, new OreVentPreExtractionDoAfterEvent(), entity.Owner, entity.Owner, used: scannerUid)
        {
            BreakOnDamage = true,

            BreakOnMove = true,
            NeedHand = true,
            BreakOnDropItem = true,

            RequireCanInteract = true,

            AttemptFrequency = AttemptFrequency.EveryTick
        });

        if (!success)
            return;

        _popupSystem.PopupEntity(
            KsPopupMessage.Create("ks-specific-orevent-startingextraction-user", ("vent", entity.Owner)), entity, args.Actor, type: PopupType.LargeCaution);
        _popupSystem.PopupEntity(
            KsPopupMessage.Create("ks-specific-orevent-startingextraction-others", ("vent", entity.Owner), ("user", Identity.Name(args.Actor, EntityManager, viewer: null))), entity, Filter.PvsExcept(args.Actor), true, type: PopupType.MediumCaution);
    }

    [SubscribeLocalEvent]
    private void OnAttemptPreExtractionDoAfter(Entity<OreVentComponent> entity, ref DoAfterAttemptEvent<OreVentPreExtractionDoAfterEvent> args)
    {
        if (args.Cancelled)
            return;

        if (!entity.Comp.Tapped &&
            !entity.Comp.BeingTapped &&
            !entity.Comp.DoingClearing)
            return;

        args.Cancel();
    }

    [SubscribeLocalEvent]
    private void OnPreExtractionDoAfter(Entity<OreVentComponent> entity, ref OreVentPreExtractionDoAfterEvent args)
    {
        if (args.Cancelled)
            return;

        var activeComponent = EnsureComp<ActiveClearingOreVentComponent>(entity.Owner);
        activeComponent.Iteration = 0;
        activeComponent.IterationDelay = entity.Comp.ClearingDuration / entity.Comp.ClearingIterations;
        Dirty(entity.Owner, activeComponent);

        args.Handled = true;
        _jitteringSystem.AddJitter(entity.Owner, amplitude: -8, frequency: 80);

        entity.Comp.DoingClearing = true;
        DirtyField(entity.Owner, entity.Comp, nameof(entity.Comp.DoingClearing));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var eqe = EntityQueryEnumerator<ActiveClearingOreVentComponent, OreVentComponent>();
        while (eqe.MoveNext(out var uid, out var activeComponent, out var component))
        {
            if (_gameTiming.CurTime < activeComponent.NextIteration)
                continue;

            if (activeComponent.Iteration < component.ClearingIterations)
            {
                // Clear the area around it, a little further out each iteration
                ClearAreaAround(uid, component.ClearRadius / (float)(component.ClearingIterations - activeComponent.Iteration));

                // on last iteration, dont repeat and instead fall thru
                activeComponent.Iteration += 1;
                activeComponent.NextIteration = _gameTiming.CurTime + activeComponent.IterationDelay;

                if (activeComponent.Iteration != component.ClearingIterations)
                    continue;
            }

            RemCompDeferred<ActiveClearingOreVentComponent>(uid);

            StartTapping((uid, component));
            RemCompDeferred<JitteringComponent>(uid);

            component.DoingClearing = false;
            DirtyField(uid, component, nameof(component.DoingClearing));
        }
    }

    private void ClearAreaAround(EntityUid uid, float radius)
    {
        var clearingEv = new OreVentClearingEvent(radius);
        RaiseLocalEvent(uid, ref clearingEv);
    }
}

/// <summary>
///     Raised on a vent when the rock around it, within <see cref="Radius"/>, should be destroyed: on each iteration
///         of clearing, and when tapping fails. Rock is server-only, so the server handles this.
/// </summary>
[ByRefEvent]
public readonly record struct OreVentClearingEvent(float Radius);
