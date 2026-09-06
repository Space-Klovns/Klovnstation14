using Content.Shared._KS14.Atmos.ChemicalFire;
using Content.Shared.Atmos;

namespace Content.Server._KS14.Atmos.ChemicalFire;

/// <summary>
///     Burns gas off the tile a chemfire occupies, hanging off the same
///         <see cref="ChemicalFireHeatTileEvent"/> that drives the ignition.
/// </summary>
public sealed partial class ChemicalFireGasConsumerSystem : EntitySystem
{
    [Dependency] private ChemicalFireSystem _chemicalFireSystem = default!;

    [Dependency] private EntityQuery<ChemicalFireComponent> _chemicalFireQuery = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ChemicalFireGasConsumerComponent, ChemicalFireHeatTileEvent>(OnHeatTile);
        SubscribeLocalEvent<ChemicalFireGasConsumerComponent, ChemicalFireCanSustainEvent>(OnCanSustain);
    }

    /// <summary>
    ///     Vetoes survival unless at least one of <see cref="ChemicalFireGasConsumerComponent.Gases"/> clears
    ///         the same trace threshold upstream's own fuel/oxidiser hotspot check uses - mirrors the
    ///         "extinguish only once every gas hit zero" condition in <see cref="OnHeatTile"/> so the two can't
    ///         drift apart.
    /// </summary>
    private void OnCanSustain(Entity<ChemicalFireGasConsumerComponent> entity, ref ChemicalFireCanSustainEvent args)
    {
        if (args.Mixture is not { } mixture || mixture.Immutable)
        {
            args.Deny();
            return;
        }

        foreach (var gas in entity.Comp.Gases.Keys)
        {
            if (mixture.GetMoles(gas) >= Atmospherics.Epsilon)
                return;
        }

        args.Deny();
    }

    private void OnHeatTile(Entity<ChemicalFireGasConsumerComponent> entity, ref ChemicalFireHeatTileEvent args)
    {
        if (args.Mixture is not { } mixture || mixture.Immutable)
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

        if (consumedAmount != 0f ||
            !entity.Comp.ExtinguishWhenDepleted ||
            !_chemicalFireQuery.TryGetComponent(entity.Owner, out var fireComponent))
            return;

        _chemicalFireSystem.ExtinguishChemicalFire((entity.Owner, fireComponent));
    }
}
