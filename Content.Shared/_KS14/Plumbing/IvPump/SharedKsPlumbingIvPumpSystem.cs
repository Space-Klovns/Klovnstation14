using Content.Shared._KS14.Chain;
using Content.Shared._Starlight.Plumbing.Components;
using Content.Shared.ActionBlocker;
using Content.Shared.Body.Components;
using Content.Shared.DragDrop;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Popups;

namespace Content.Shared._KS14.Plumbing.IvPump;

public abstract class SharedKsPlumbingIvPumpSystem : EntitySystem
{
    [Dependency] private readonly SharedAppearanceSystem _appearanceSystem = default!;
    [Dependency] private readonly SharedPopupSystem _popupSystem = default!;
    [Dependency] private readonly ActionBlockerSystem _actionBlockerSystem = default!;

    [Dependency] protected readonly EntityQuery<BloodstreamComponent> BloodstreamQuery = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<KsPlumbingIvPumpComponent, ExaminedEvent>(OnExamined);

        SubscribeLocalEvent<KsPlumbingIvPumpComponent, CanDragEvent>(OnCanDrag);
        SubscribeLocalEvent<KsPlumbingIvPumpComponent, CanDropDraggedEvent>(OnCanDropDragged);

        SubscribeLocalEvent<KsPlumbingIvPumpComponent, ActivateInWorldEvent>(OnActivateInWorld);
    }

    private void OnExamined(Entity<KsPlumbingIvPumpComponent> entity, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("ks-plumbing-ivpump-examined", ("mode", entity.Comp.Mode.ToString())), priority: 5);
    }

    protected void UpdateState(Entity<KsPlumbingIvPumpComponent?> entity)
    {
        if (!Resolve(entity, ref entity.Comp))
            return;

        var active = entity.Comp.ChainStartUid is { };
        if (TryComp<PlumbingOutletComponent>(entity.Owner, out var outletComponent))
        {
            outletComponent.Enabled = active;
            Dirty(entity.Owner, outletComponent);
        }

        if (TryComp<AppearanceComponent>(entity.Owner, out var appearanceComponent))
        {
            _appearanceSystem.SetData(entity.Owner, KsPlumbingIvPumpVisuals.Active, active, component: appearanceComponent);
            _appearanceSystem.SetData(entity.Owner, KsPlumbingIvPumpVisuals.Injecting, entity.Comp.Mode == KsPlumbingIvPumpMode.Injecting, component: appearanceComponent);
        }
    }

    private void SetMode(Entity<KsPlumbingIvPumpComponent> entity, KsPlumbingIvPumpMode newMode, EntityUid userUid)
    {
        if (entity.Comp.Mode == newMode)
            return;

        switch (newMode)
        {
            case KsPlumbingIvPumpMode.Injecting:
                _popupSystem.PopupPredicted(Loc.GetString("ks-plumbing-ivpump-setto-injecting"), entity.Owner, userUid, type: PopupType.Small);
                break;
            case KsPlumbingIvPumpMode.Drawing:
                _popupSystem.PopupPredicted(Loc.GetString("ks-plumbing-ivpump-setto-drawing"), entity.Owner, userUid, type: PopupType.Small);
                break;
            default:
                throw new InvalidOperationException("Invalid mode for IV pump: " + newMode.ToString());
        }

        entity.Comp.Mode = newMode;
        Dirty(entity);
        UpdateState(entity!);
    }

    private void OnCanDrag(Entity<KsPlumbingIvPumpComponent> entity, ref CanDragEvent args)
    {
        args.Handled = true;
    }

    private void OnCanDropDragged(Entity<KsPlumbingIvPumpComponent> entity, ref CanDropDraggedEvent args)
    {
        args.Handled = true;
        if (args.CanDrop)
            return;

        // not if dey are alr linked
        if (HasComp<ChainEdgeComponent>(args.Target))
            return;

        args.CanDrop |= BloodstreamQuery.HasComponent(args.Target) &&
            _actionBlockerSystem.CanComplexInteract(args.User);
    }

    private void OnActivateInWorld(Entity<KsPlumbingIvPumpComponent> entity, ref ActivateInWorldEvent args)
    {
        if (!args.Complex ||
            args.Handled)
            return;

        switch (entity.Comp.Mode)
        {
            case KsPlumbingIvPumpMode.Injecting:
                SetMode(entity, KsPlumbingIvPumpMode.Drawing, args.User);
                break;
            case KsPlumbingIvPumpMode.Drawing:
                SetMode(entity, KsPlumbingIvPumpMode.Injecting, args.User);
                break;
            default:
                return;
        }

        args.Handled = true;
    }
}
