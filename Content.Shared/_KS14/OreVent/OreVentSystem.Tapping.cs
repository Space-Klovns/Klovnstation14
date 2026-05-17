using System.Numerics;
using Content.Shared._KS14.OreVent.Drone;
using Content.Shared.DoAfter;
using Robust.Shared.Map;

namespace Content.Shared._KS14.OreVent;

public sealed partial class OreVentSystem : EntitySystem
{
    private void InitialiseTapping()
    {

        SubscribeLocalEvent<OreVentComponent, MapInitEvent>(OnMapInit);

        SubscribeLocalEvent<OreVentComponent, OreVentTappingDoAfterEvent>(OnOreVentTappingDoAfter);
        SubscribeLocalEvent<OreVentComponent, DoAfterAttemptEvent<OreVentTappingDoAfterEvent>>(OnOreVentTappingDoAfterAttempt);

        SubscribeLocalEvent<OreVentComponent, OreVentDroneDestroyedEvent>(OnDroneDestroyed);
    }

    private void OnMapInit(Entity<OreVentComponent> entity, ref MapInitEvent args)
    {
        if (!entity.Comp.Tapped)
            return;

        _appearanceSystem.SetData(entity.Owner, OreVentVisuals.Tapped, true);
        _oreWellSystem.GenerateOreWellWithSettings(entity.Owner, entity.Comp.OreWellSettingId);
    }


    private void OnOreVentTappingDoAfter(Entity<OreVentComponent> entity, ref OreVentTappingDoAfterEvent args)
    {
        if (args.Cancelled)
        {
            CancelTapping(entity!);

            _popupSystem.PopupEntity(Loc.GetString("ks-specific-orevent-tapping-failed-destruction", ("vent", entity.Owner)), entity.Owner, type: Popups.PopupType.MediumCaution);
            ClearAreaAround(entity, entity.Comp.ClearRadius / 2f);

            return;
        }

        SucceedTapping(entity.Owner);
    }

    private void OnOreVentTappingDoAfterAttempt(Entity<OreVentComponent> entity, ref DoAfterAttemptEvent<OreVentTappingDoAfterEvent> args)
    {
        if (args.Cancelled ||
            entity.Comp.BeingTapped)
            return;

        // Cancel if its no longer being tapped for whatever reason
        args.Cancel();
    }

    private void OnDroneDestroyed(Entity<OreVentComponent> entity, ref OreVentDroneDestroyedEvent args)
    {
        if (!entity.Comp.BeingTapped)
            return;

        CancelTapping(entity!);
    }

    public void StartTapping(Entity<OreVentComponent?> entity)
    {
        if (!Resolve(entity.Owner, ref entity.Comp))
            return;

        var metaDataComponent = MetaData(entity.Owner);
        entity.Comp.BeingTapped = true;
        DirtyField(entity.Owner, entity.Comp, nameof(entity.Comp.BeingTapped), meta: metaDataComponent);

        entity.Comp.TappingFinishedTime = _gameTiming.CurTime + entity.Comp.ExtractionDuration;
        DirtyField(entity.Owner, entity.Comp, nameof(entity.Comp.TappingFinishedTime), meta: metaDataComponent);

        StartWaveDefense(entity!);

        if (entity.Comp.DroneEntityProto is { } droneProto &&
            _netManager.IsServer)
        {
            var ventTransformComponent = Transform(entity.Owner);
            var droneUid = Spawn(droneProto, new(ventTransformComponent.ParentUid, ventTransformComponent.LocalPosition));
            entity.Comp.DroneEntityUid = droneUid;

            _oreVentDroneSystem.Arrive(droneUid, entity.Owner);
        }

        // Start doafter
        _doAfterSystem.TryStartDoAfter(new DoAfterArgs(EntityManager, entity.Owner, entity.Comp.ExtractionDuration, new OreVentTappingDoAfterEvent(), entity.Owner, entity.Owner, used: null)
        {
            BreakOnDamage = true,
            DistanceThreshold = null,

            // because the doafter is on the ore vent
            BreakOnMove = false,
            NeedHand = false,
            BreakOnDropItem = false,

            RequireCanInteract = false,
            Hidden = true,

            AttemptFrequency = AttemptFrequency.EveryTick
        });
    }

    private void StartWaveDefense(Entity<OreVentComponent> entity)
    {
        if (!_netManager.IsServer)
            return;

        var processEntityUid = SpawnAtPosition(entity.Comp.TappingProcessEntityProto, new EntityCoordinates(entity, Vector2.Zero));
        entity.Comp.TappingProcessEntityUid = processEntityUid;
    }

    /// <summary>
    ///     Called when tapping is stopped, regardless of success or failure.
    /// </summary>
    private void OnTappingEnded(Entity<OreVentComponent> entity, MetaDataComponent? metaDataComponent = null)
    {
        entity.Comp.BeingTapped = false;
        DirtyField(entity.Owner, entity.Comp, nameof(entity.Comp.BeingTapped), meta: metaDataComponent);

        QueueDel(entity.Comp.TappingProcessEntityUid);
        entity.Comp.TappingProcessEntityUid = null;

        if (entity.Comp.DroneEntityUid is { } droneUid &&
            !TerminatingOrDeleted(droneUid))
            _oreVentDroneSystem.Escape(droneUid);

        entity.Comp.DroneEntityUid = null;
    }

    /// <summary>
    ///     Assumes the entity is currently being tapped.
    ///         This will finish the tapping process, as a success.
    /// </summary>
    public void SucceedTapping(Entity<OreVentComponent?> entity, MetaDataComponent? metaDataComponent = null)
    {
        if (!Resolve(entity.Owner, ref entity.Comp) ||
            !EntityManager.MetaQuery.Resolve(entity.Owner, ref metaDataComponent))
            return;

        // Change necessary vars
        OnTappingEnded(entity!, metaDataComponent: metaDataComponent);

        entity.Comp.Tapped = true;
        DirtyField(entity.Owner, entity.Comp, nameof(entity.Comp.Tapped), meta: metaDataComponent);

        _oreWellSystem.GenerateOreWellWithSettings(entity.Owner, entity.Comp.OreWellSettingId);
        _appearanceSystem.SetData(entity.Owner, OreVentVisuals.Tapped, true);
    }

    /// <summary>
    ///     Assumes the entity is currently being tapped.
    ///         This will prematurely cancel the tapping process, as a failure.
    /// </summary>
    public void CancelTapping(Entity<OreVentComponent?> entity, MetaDataComponent? metaDataComponent = null)
    {
        if (!Resolve(entity.Owner, ref entity.Comp) ||
            !EntityManager.MetaQuery.Resolve(entity.Owner, ref metaDataComponent))
            return;

        OnTappingEnded(entity!, metaDataComponent: metaDataComponent);
    }
}
