using Content.Shared._KS14.ScanDiscoverable.Base;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Jittering;
using Content.Shared.Popups;
using Robust.Shared.Player;

namespace Content.Shared._KS14.OreVent;

public sealed class OreVentSystem : EntitySystem
{
    [Dependency] private readonly SharedDoAfterSystem _doAfterSystem = default!;
    [Dependency] private readonly SharedJitteringSystem _jitteringSystem = default!;
    [Dependency] private readonly SharedPopupSystem _popupSystem = default!;
    [Dependency] private readonly KsScanDiscoverableSystem _discoverableSystem = default!;
    [Dependency] private readonly OreWellSystem _oreWellSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<OreVentComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<OreVentComponent, OreVentPreExtractionEvent>(OnPreExtraction);
    }

    private void OnInteractUsing(Entity<OreVentComponent> entity, ref InteractUsingEvent args)
    {
        if (args.Handled ||
            entity.Comp.DoingPreExtraction ||
            !_discoverableSystem.IsScanner(args.Used))
            return;

        if (entity.Comp.Tapped)
        {
            _popupSystem.PopupPredicted(
                Loc.GetString(Loc.GetString("ks-specific-orevent-alreadytapped")), entity, args.User, Filter.PvsExcept(args.User), true);

            return;
        }

        if (!_discoverableSystem.IsDiscovered(entity))
            return;

        if (entity.Comp.BeingTapped)
        {
            _popupSystem.PopupPredicted(
                Loc.GetString(Loc.GetString("ks-specific-orevent-whatareyoudoing")), entity, args.User, Filter.PvsExcept(args.User), true, type: PopupType.SmallCaution);

            return;
        }

        var success = _doAfterSystem.TryStartDoAfter(new DoAfterArgs(EntityManager, entity.Owner, entity.Comp.PreExtractionDuration, new OreVentPreExtractionEvent(), entity.Owner, entity.Owner, used: args.Used)
        {
            BreakOnDamage = true,

            // max 1 tile distance between used item and doafter whateverburger
            DistanceThreshold = 1f,

            // because the doafter is on the ore vent
            BreakOnMove = false,
            NeedHand = false,
            BreakOnDropItem = false
        });
        if (!success)
            return;

        args.Handled = true;
        _jitteringSystem.AddJitter(entity.Owner, amplitude: -8, frequency: 80);

        entity.Comp.DoingPreExtraction = true;
        DirtyField(entity.Owner, entity.Comp, nameof(entity.Comp.DoingPreExtraction));
    }

    private void OnPreExtraction(Entity<OreVentComponent> entity, ref OreVentPreExtractionEvent args)
    {
        RemCompDeferred<JitteringComponent>(entity);

        entity.Comp.DoingPreExtraction = false;
        DirtyField(entity.Owner, entity.Comp, nameof(entity.Comp.DoingPreExtraction));

        if (args.Cancelled)
            return;

        entity.Comp.BeingTapped = true;
        DirtyField(entity.Owner, entity.Comp, nameof(entity.Comp.BeingTapped));

        _oreWellSystem.StartExtraction(entity.Owner);
    }
}
