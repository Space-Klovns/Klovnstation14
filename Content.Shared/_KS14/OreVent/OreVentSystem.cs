using Content.Shared._KS14.OreVent.Drone;
using Content.Shared._KS14.OreWell;
using Content.Shared._KS14.ScanDiscoverable.Base;
using Content.Shared.DoAfter;
using Content.Shared.Explosion.EntitySystems;
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
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly INetManager _netManager = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfterSystem = default!;
    [Dependency] private readonly SharedJitteringSystem _jitteringSystem = default!;
    [Dependency] private readonly SharedPopupSystem _popupSystem = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearanceSystem = default!;
    [Dependency] private readonly SharedExplosionSystem _explosionSystem = default!;
    [Dependency] private readonly KsScanDiscoverableSystem _discoverableSystem = default!;
    [Dependency] private readonly SharedOreVentDroneSystem _oreVentDroneSystem = default!;
    [Dependency] private readonly OreWellSystem _oreWellSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        InitialiseTapping();

        SubscribeLocalEvent<OreVentComponent, InteractUsingEvent>(OnInteractUsing);

        SubscribeLocalEvent<OreVentComponent, OreVentPreExtractionDoAfterEvent>(OnPreExtractionDoAfter);
        SubscribeLocalEvent<OreVentComponent, DoAfterAttemptEvent<OreVentPreExtractionDoAfterEvent>>(OnAttemptPreExtractionDoAfter);

        SubscribeLocalEvent<OreVentComponent, OreVentClearingDoAfterEvent>(OnClearingDoAfter);
    }

    private void OnInteractUsing(Entity<OreVentComponent> entity, ref InteractUsingEvent args)
    {
        if (args.Handled ||
            entity.Comp.DoingClearing ||
            !_discoverableSystem.IsScanner(args.Used))
            return;

        if (entity.Comp.Tapped)
        {
            _popupSystem.PopupPredicted(
                Loc.GetString("ks-specific-orevent-alreadytapped"), entity, args.User, Filter.PvsExcept(args.User), true);

            return;
        }

        if (!_discoverableSystem.IsDiscovered(entity))
            return;

        if (entity.Comp.BeingTapped)
        {
            _popupSystem.PopupPredicted(
                Loc.GetString("ks-specific-orevent-whatareyoudoing"), entity, args.User, Filter.PvsExcept(args.User), true, type: PopupType.SmallCaution);

            return;
        }

        var success = _doAfterSystem.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, entity.Comp.ClearingDuration, new OreVentPreExtractionDoAfterEvent(), entity.Owner, entity.Owner, used: args.Used)
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

        if (_netManager.IsClient)
            _popupSystem.PopupClient(
                Loc.GetString("ks-specific-orevent-user", ("vent", entity.Owner)), entity, args.User, type: PopupType.LargeCaution);
        else
            _popupSystem.PopupEntity(
                Loc.GetString("ks-specific-orevent-others", ("vent", entity.Owner), ("user", Identity.Name(args.User, EntityManager, viewer: null))), entity, Filter.PvsExcept(args.User), true, type: PopupType.MediumCaution);
    }

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

    private void OnPreExtractionDoAfter(Entity<OreVentComponent> entity, ref OreVentPreExtractionDoAfterEvent args)
    {
        if (args.Cancelled)
            return;

        var success = _doAfterSystem.TryStartDoAfter(new DoAfterArgs(EntityManager, entity.Owner, entity.Comp.ClearingDuration / entity.Comp.ClearingIterations, new OreVentClearingDoAfterEvent(0), entity.Owner, entity.Owner, used: null)
        {
            BreakOnDamage = true,
            DistanceThreshold = null,

            // because the doafter is on the ore vent
            BreakOnMove = false,
            NeedHand = false,
            BreakOnDropItem = false,

            RequireCanInteract = false,
            Hidden = true
        });
        if (!success)
            return;

        args.Handled = true;
        _jitteringSystem.AddJitter(entity.Owner, amplitude: -8, frequency: 80);

        entity.Comp.DoingClearing = true;
        DirtyField(entity.Owner, entity.Comp, nameof(entity.Comp.DoingClearing));
    }

    private void OnClearingDoAfter(Entity<OreVentComponent> entity, ref OreVentClearingDoAfterEvent args)
    {
        if (args.Cancelled)
            goto endExtraction;

        if (args.Iteration < entity.Comp.ClearingIterations)
        {
            // Clear the area around ts
            // TODO LCDC: Something better than explosions
            ClearAreaAround(entity, entity.Comp.ClearRadius / (float)(entity.Comp.ClearingIterations - args.Iteration));

            // on last iteration, dont repeat and instead fall thru
            args.Iteration += 1;
            if (args.Iteration != entity.Comp.ClearingIterations)
            {
                args.Repeat = true;
                return;
            }
        }

        StartTapping(entity!);

    endExtraction:
        RemCompDeferred<JitteringComponent>(entity);

        entity.Comp.DoingClearing = false;
        DirtyField(entity.Owner, entity.Comp, nameof(entity.Comp.DoingClearing));
    }

    private void ClearAreaAround(Entity<OreVentComponent> entity, float radius)
    {
        _explosionSystem.TriggerExplosive(entity.Owner, delete: false, radius: radius);
        Log.Debug($"cleared area {radius}");
    }
}
