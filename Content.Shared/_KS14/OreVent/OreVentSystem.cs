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

        args.Handled = true;

        if (_netManager.IsClient)
            _popupSystem.PopupClient(
                Loc.GetString("ks-specific-orevent-startingextraction-user", ("vent", entity.Owner)), entity, args.User, type: PopupType.LargeCaution);
        else
            _popupSystem.PopupEntity(
                Loc.GetString("ks-specific-orevent-startingextraction-others", ("vent", entity.Owner), ("user", Identity.Name(args.User, EntityManager, viewer: null))), entity, Filter.PvsExcept(args.User), true, type: PopupType.MediumCaution);
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
                // Clear the area around ts
                // TODO LCDC: Something better than explosions
                ClearAreaAround((uid, component), component.ClearRadius / (float)(component.ClearingIterations - activeComponent.Iteration));

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

    private void ClearAreaAround(Entity<OreVentComponent> entity, float radius)
    {
        _explosionSystem.TriggerExplosive(entity.Owner, delete: false, radius: radius);
    }
}
