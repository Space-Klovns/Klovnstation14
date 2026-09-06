using Content.Shared.Electrocution;
using Content.Shared.Construction;
using Content.Shared._KS14.Construction;
using Content.Server.Electrocution;
using Content.Server.Lightning;
using Content.Shared._KS14.ArcFlash.Components;
using Content.Shared._KS14.ArcFlash;
using Content.Shared._KS14.Power;

namespace Content.Server._KS14.ArcFlash;

public sealed partial class ArcFlashSystem : SharedArcFlashSystem
{
    [Dependency] private ElectrocutionSystem _electrocutionSystem = default!;
    [Dependency] private LightningSystem _lightning = default!;

    public override void Initialize()
    {
        base.Initialize();



        SubscribeLocalEvent<ArcFlashAnchorableComponent, AnchorStateChangedEvent>(OnAnchorChanged);
        SubscribeLocalEvent<ArcFlashDeconstructableComponent, MachineDeconstructedEvent>(OnDeconstruction);
        SubscribeLocalEvent<ArcFlashDeconstructableComponent, APCDeconstructedEvent>(OnAPCDeconstruction);
    }

    protected override void OnAttemptCutCable(Entity<ArcFlashCableComponent> entity, ref AttemptCutCableEvent args)
    {
        base.OnAttemptCutCable(entity, ref args);

    }

    private void OnAnchorChanged(Entity<ArcFlashAnchorableComponent> entity, ref AnchorStateChangedEvent args)
    {
        if (args.Anchored)
            return; // we don't want to inflict arc flashing when the connection is created

        // anchor state can change as a result of deletion (detach to null) - same shit as cable system
        if (TerminatingOrDeleted(entity) ||
            !TryComp<ElectrifiedComponent>(entity, out var electrifiedComponent) ||
            !_electrocutionSystem.IsPowered(entity.Owner, electrifiedComponent, Transform(entity)))
            return;

        DoLightning((entity, entity));
    }
    private void OnDeconstruction(Entity<ArcFlashDeconstructableComponent> entity, ref MachineDeconstructedEvent args)
    {
        //there is no way for us to check battery status anyway
        DoLightning((entity, entity));
    }
    private void OnAPCDeconstruction(Entity<ArcFlashDeconstructableComponent> entity, ref APCDeconstructedEvent args)
    {
        //there is no way for us to check battery status anyway
        DoLightning((entity, entity));
    }

    private void DoLightning(Entity<BaseArcFlashImpactComponent> entity)
        => _lightning.ShootRandomLightnings(entity, entity.Comp.LightningRange, entity.Comp.LightningAmount, lightningPrototype: entity.Comp.LightningPrototype);
}
