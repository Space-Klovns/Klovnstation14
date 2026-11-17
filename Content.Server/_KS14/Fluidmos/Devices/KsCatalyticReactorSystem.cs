using Content.Server._KS14.Fluidmos.Reactions;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Shared._KS14.Fluidmos.Reactions;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Examine;

using Content.Shared.Atmos.Piping.Components;

namespace Content.Server._KS14.Fluidmos.Devices;

/// <summary>
///     Runs <see cref="KsCatalyticReactorComponent"/>s: steps the bed's reactions on the reactor's pipe net, and lets the
///         bed take up whatever poisons it.
/// </summary>
public sealed partial class KsCatalyticReactorSystem : EntitySystem
{
    [Dependency] private NodeContainerSystem _nodeContainerSystem = default!;
    [Dependency] private ItemSlotsSystem _itemSlotsSystem = default!;
    [Dependency] private KsFluidReactionSystem _reactionSystem = default!;
    [Dependency] private KsFluidCatalystSystem _catalystSystem = default!;
    [Dependency] private SharedAppearanceSystem _appearanceSystem = default!;
    [Dependency] private EntityQuery<KsFluidCatalystComponent> _catalystQuery = default!;

    [SubscribeLocalEvent]
    private void OnDeviceUpdate(Entity<KsCatalyticReactorComponent> entity, ref AtmosDeviceUpdateEvent args)
    {
        SetReacting(entity, React(entity, args.dt));
    }

    [SubscribeLocalEvent]
    private void OnDeviceDisabled(Entity<KsCatalyticReactorComponent> entity, ref AtmosDeviceDisabledEvent args)
    {
        SetReacting(entity, false);
    }

    private bool React(Entity<KsCatalyticReactorComponent> entity, float deltaTime)
    {
        if (!TryGetBed(entity, out var bed) ||
            bed.Comp.Activity <= 0f ||
            !_nodeContainerSystem.TryGetNode(entity.Owner, entity.Comp.NodeName, out PipeNode? node) ||
            node.Air.Volume <= 0f)
            return false;

        var mixture = node.Air;
        var share = MathF.Min(1f, entity.Comp.BedVolume / mixture.Volume);

        var reacted = _reactionSystem.ReactCatalysed(mixture, bed.Comp.Catalyst, deltaTime * share * bed.Comp.Activity);
        TakeUpPoisons(bed, mixture, share, deltaTime);
        return reacted;
    }

    private void SetReacting(Entity<KsCatalyticReactorComponent> entity, bool reacting)
    {
        if (entity.Comp.Reacting == reacting)
            return;

        entity.Comp.Reacting = reacting;
        _appearanceSystem.SetData(entity.Owner, KsCatalyticReactorVisuals.Reacting, reacting);
    }

    /// <summary>
    ///     The bed binds a share of each poison in the net every second, and loses activity for every unit it binds.
    /// </summary>
    private void TakeUpPoisons(Entity<KsFluidCatalystComponent> bed, GasMixture mixture, float share, float deltaTime)
    {
        if (mixture.Fluid is not { IsEmpty: false } fluid)
            return;

        var catalyst = ProtoMan.Index(bed.Comp.Catalyst);
        if (catalyst.Poisons.Count == 0)
            return;

        var uptakeFraction = 1f - MathF.Exp(-catalyst.PoisonUptakeRate * share * deltaTime);
        var activityLost = 0f;

        foreach (var (poison, activityPerUnit) in catalyst.Poisons)
        {
            var units = _reactionSystem.TotalUnits(mixture, fluid, poison) * uptakeFraction;
            if (units <= 0f)
                continue;

            _reactionSystem.RemoveUnits(mixture, fluid, poison, units);
            activityLost += units * activityPerUnit;
        }

        _catalystSystem.Poison(bed, activityLost);
    }

    private bool TryGetBed(Entity<KsCatalyticReactorComponent> entity, out Entity<KsFluidCatalystComponent> bed)
    {
        bed = default;
        if (_itemSlotsSystem.GetItemOrNull(entity.Owner, entity.Comp.CatalystSlotId) is not { } bedUid ||
            !_catalystQuery.TryComp(bedUid, out var catalystComponent))
            return false;

        bed = (bedUid, catalystComponent);
        return true;
    }

    [SubscribeLocalEvent]
    private void OnExamined(Entity<KsCatalyticReactorComponent> entity, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        using (args.PushGroup(nameof(KsCatalyticReactorComponent)))
        {
            if (TryGetBed(entity, out var bed))
            {
                args.PushMarkup(Loc.GetString("ks-catalytic-reactor-examine-bed",
                    ("catalyst", Loc.GetString(ProtoMan.Index(bed.Comp.Catalyst).Name)),
                    ("activity", (int)MathF.Round(bed.Comp.Activity * 100f))));
            }
            else
            {
                args.PushMarkup(Loc.GetString("ks-catalytic-reactor-examine-empty"));
            }

            if (_nodeContainerSystem.TryGetNode(entity.Owner, entity.Comp.NodeName, out PipeNode? node) && node.Air.Volume > 0f)
            {
                args.PushMarkup(Loc.GetString("ks-catalytic-reactor-examine-conditions",
                    ("temperature", (int)MathF.Round(node.Air.Temperature)),
                    ("pressure", (int)MathF.Round(node.Air.Pressure)),
                    ("share", (int)MathF.Round(MathF.Min(1f, entity.Comp.BedVolume / node.Air.Volume) * 100f))));
            }
        }
    }
}
