using Content.Server.Atmos.EntitySystems;
using Content.Shared._KS14.Atmos.ChemicalFire;

namespace Content.Server._KS14.Atmos.ChemicalFire;

/// <summary>
///     Burns gas off the tile a chemfire occupies, hanging off the same
///         <see cref="ChemicalFireHeatTileEvent"/> that drives the ignition.
/// </summary>
public sealed partial class ChemicalFireGasConsumerSystem : EntitySystem
{
    [Dependency] private AtmosphereSystem _atmosphereSystem = default!;
    [Dependency] private ChemicalFireSystem _chemicalFireSystem = default!;

    [Dependency] private EntityQuery<ChemicalFireComponent> _chemicalFireQuery = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ChemicalFireGasConsumerComponent, ChemicalFireHeatTileEvent>(OnHeatTile);
    }

    private void OnHeatTile(Entity<ChemicalFireGasConsumerComponent> entity, ref ChemicalFireHeatTileEvent args)
    {
        var mixture = _atmosphereSystem.GetTileMixture((args.GridUid, null, null), null, args.Tile, excite: true);
        if (mixture is null || mixture.Immutable)
            return;

        var consumedAnything = false;
        foreach (var (gas, molesPerSecond) in entity.Comp.Gases)
        {
            var available = mixture.GetMoles(gas);
            if (available <= 0f)
                continue;

            mixture.AdjustMoles(gas, -MathF.Min(available, molesPerSecond * args.Seconds));
            consumedAnything = true;
        }

        if (entity.Comp.ProducedGases is { } producedGases)
        {
            foreach (var (gas, molesPerSecond) in producedGases)
                mixture.AdjustMoles(gas, molesPerSecond * args.Seconds);
        }

        if (consumedAnything ||
            !entity.Comp.ExtinguishWhenDepleted ||
            !_chemicalFireQuery.TryGetComponent(entity.Owner, out var fireComponent))
            return;

        _chemicalFireSystem.ExtinguishChemicalFire((entity.Owner, fireComponent));
    }
}
