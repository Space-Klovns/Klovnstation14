using Content.Shared.Atmos;

namespace Content.Shared._KS14.Atmos.ChemicalFire;

/// <summary>
///     Burns gas off the tile a chemfire occupies, hanging off the same
///         <see cref="ChemicalFireHeatTileEvent"/> that drives the ignition. Runs on both client and server -
///         the gas mixture itself only ever exists server-side, so <see cref="OnHeatTile"/> is a no-op on the
///         client, but <see cref="OnCanSustain"/> still needs to be reachable there since chemfires spawn
///         through prediction.
/// </summary>
public sealed partial class ChemicalFireGasConsumerSystem : EntitySystem
{
    [Dependency] private SharedChemicalFireSystem _chemicalFireSystem = default!;
    [Dependency] private EntityQuery<ChemicalFireComponent> _chemicalFireQuery = default!;

    /// <summary>
    ///     Vetoes survival unless at least one of <see cref="ChemicalFireGasConsumerComponent.Gases"/> clears
    ///         the same trace threshold upstream's own fuel/oxidiser hotspot check uses - mirrors the
    ///         "extinguish only once every gas hit zero" condition in <see cref="OnHeatTile"/> so the two can't
    ///         drift apart.
    /// </summary>
    /// <remarks>
    ///     A missing mixture is treated as "unknown", not "no gas" - this is what happens on the client, which
    ///         has no atmos data to check against; it predicts optimistically and lets the server's own answer
    ///         correct it if it turns out to be wrong. An immutable mixture (space) is real data, so that still
    ///         vetoes.
    /// </remarks>
    [SubscribeLocalEvent]
    private void OnCanSustain(Entity<ChemicalFireGasConsumerComponent> entity, ref ChemicalFireCanSustainEvent args)
    {
        if (args.Mixture is not { } mixture)
            return;

        if (mixture.Immutable)
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

    [SubscribeLocalEvent]
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
