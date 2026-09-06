using Content.Shared.Electrocution;
using Content.Shared.Construction;
using Content.Shared.NodeContainer;
using Content.Shared._KS14.Construction;
using Content.Server.Electrocution;
using Content.Server.Lightning;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.Power.NodeGroups;
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

        if (!IsCablePowered(entity))
            return;

        DoLightning((entity, entity));
    }

    /// <summary>
    ///     Point-in-time check for whether an entity's node container currently has a node
    ///         with live current flowing through its power net (HV/MV/APC).
    /// </summary>
    public bool IsCablePowered(EntityUid uid)
    {
        if (!TryComp<NodeContainerComponent>(uid, out var nodeContainerComponent))
            return false;

        foreach (var node in nodeContainerComponent.Nodes.Values)
        {
            if (node.NodeGroup is IBasePowerNet { NetworkNode.LastCombinedMaxSupply: > 0 })
                return true;
        }

        return false;
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
