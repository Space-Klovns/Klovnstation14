using Content.Shared.Electrocution;
using Content.Shared.Construction;
using Content.Shared.NodeContainer;
using Content.Shared._KS14.Construction;
using Content.Server.Electrocution;
using Content.Server.Lightning;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.Power.Components;
using Content.Server.Power.NodeGroups;
using Content.Shared._KS14.ArcFlash.Components;
using Content.Shared._KS14.ArcFlash;
using Content.Shared._KS14.Atmos.ChemicalFire;
using Content.Shared._KS14.Power;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.ArcFlash;

public sealed partial class ArcFlashSystem : SharedArcFlashSystem
{
    [Dependency] private ElectrocutionSystem _electrocutionSystem = default!;
    [Dependency] private LightningSystem _lightning = default!;
    [Dependency] private SharedChemicalFireSystem _chemicalFireSystem = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;

    [Dependency] private EntityQuery<MapGridComponent> _mapGridQuery = default!;

    private readonly List<EntityUid> _hitTargets = [];
    private readonly HashSet<EntityUid> _burningEntities = [];

    /// <summary>
    ///     The sides of a tile tried for a fire that cannot burn on its own, after the one the entity faces.
    /// </summary>
    private static readonly Direction[] CardinalDirections = [Direction.South, Direction.East, Direction.North, Direction.West];

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

    [SubscribeLocalEvent]
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

    [SubscribeLocalEvent]
    private void OnDeconstruction(Entity<ArcFlashDeconstructableComponent> entity, ref MachineDeconstructedEvent args)
    {
        //there is no way for us to check battery status anyway
        DoLightning((entity, entity));
    }

    [SubscribeLocalEvent]
    private void OnAPCDeconstruction(Entity<ArcFlashDeconstructableComponent> entity, ref APCDeconstructedEvent args)
    {
        //there is no way for us to check battery status anyway
        DoLightning((entity, entity));
    }

    /// <summary>
    ///     Arcs bolts out of the entity, and starts an electrical fire on it and on every electrical thing they hit.
    /// </summary>
    private void DoLightning(Entity<BaseArcFlashImpactComponent> entity)
    {
        _hitTargets.Clear();
        _lightning.ShootRandomLightnings(entity,
            entity.Comp.LightningRange,
            entity.Comp.LightningAmount,
            lightningPrototype: entity.Comp.LightningPrototype,
            hitTargets: _hitTargets);

        if (entity.Comp.ElectricalFirePrototype is { } firePrototype)
        {
            // A set: the same machine can be hit by several bolts, or arc back onto the emitter.
            _burningEntities.Clear();
            _burningEntities.Add(entity.Owner);
            foreach (var targetUid in _hitTargets)
            {
                if (IsElectrical(targetUid))
                    _burningEntities.Add(targetUid);
            }

            foreach (var burningUid in _burningEntities)
                TryStartElectricalFire(burningUid, firePrototype);

            _burningEntities.Clear();
        }

        _hitTargets.Clear();
    }

    /// <summary>
    ///     Whether a bolt hitting this sets it alight: anything powered or carrying power, rather than a person or a
    ///         wall.
    /// </summary>
    private bool IsElectrical(EntityUid uid)
    {
        return HasComp<ApcPowerReceiverComponent>(uid) ||
               HasComp<ApcComponent>(uid) ||
               HasComp<PowerNetworkBatteryComponent>(uid) ||
               HasComp<CableComponent>(uid);
    }

    /// <summary>
    ///     Starts a fire on the entity's tile. Something mounted on a wall cannot burn inside it, so its fire starts in
    ///         front of it instead, on the tile it faces, and failing that on whichever side of it will take one.
    /// </summary>
    /// <returns>Whether a fire was started, or was already burning there.</returns>
    public bool TryStartElectricalFire(EntityUid uid, EntProtoId firePrototype)
    {
        if (TerminatingOrDeleted(uid))
            return false;

        var transformComponent = Transform(uid);
        if (transformComponent.GridUid is not { } gridUid ||
            !_mapGridQuery.TryComp(gridUid, out var mapGridComponent))
            return false;

        var grid = new Entity<MapGridComponent?>(gridUid, mapGridComponent);
        var tile = _mapSystem.CoordinatesToTile(gridUid, mapGridComponent, transformComponent.Coordinates);

        // Not a wall: anything else that stops a fire here (no air) stops one beside it as well.
        if (!_chemicalFireSystem.IsWall(grid, tile))
            return _chemicalFireSystem.SpawnChemicalFire(firePrototype, grid, tile) != null;

        // A wallmount's front is the way it faces.
        var facing = transformComponent.LocalRotation.GetCardinalDir();
        if (_chemicalFireSystem.SpawnChemicalFire(firePrototype, grid, tile + facing.ToIntVec()) != null)
            return true;

        foreach (var direction in CardinalDirections)
        {
            if (direction != facing && _chemicalFireSystem.SpawnChemicalFire(firePrototype, grid, tile + direction.ToIntVec()) != null)
                return true;
        }

        return false;
    }
}
