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

        var consumedAmount = 0f;
        foreach (var (gas, molesPerSecond) in entity.Comp.Gases)
        {
            var available = mixture.GetMoles(gas);
            if (available <= 0f)
                continue;

            var gasConsumedAmount = MathF.Min(available, molesPerSecond * args.Seconds);
            mixture.AdjustMoles(gas, -gasConsumedAmount);

            consumedAmount += gasConsumedAmount;
        }

        // Production is a ratio, not an absolute rate - we only ever put back exactly as many moles as we took.
        if (consumedAmount > 0f && entity.Comp.ProducedGasRatios is { } producedGasRatios)
        {
            var totalRatio = 0f;
            foreach (var ratio in producedGasRatios.Values)
                totalRatio += ratio;

            if (totalRatio > 0f)
            {
                foreach (var (gas, ratio) in producedGasRatios)
                    mixture.AdjustMoles(gas, consumedAmount * (ratio / totalRatio));
            }
        }

        if (consumedAmount == 0f ||
            !entity.Comp.ExtinguishWhenDepleted ||
            !_chemicalFireQuery.TryGetComponent(entity.Owner, out var fireComponent))
            return;

        _chemicalFireSystem.ExtinguishChemicalFire((entity.Owner, fireComponent));
    }
}
