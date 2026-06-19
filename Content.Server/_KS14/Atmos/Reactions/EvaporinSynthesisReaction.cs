using Content.Server.Atmos;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Reactions;
using JetBrains.Annotations;

namespace Content.Server._KS14.Atmos.Reactions;

/// <summary>
///     Synthesis of Evaporin from Tritium and Carbon Dioxide at high temperatures.
/// </summary>
[UsedImplicitly]
[DataDefinition]
public sealed partial class EvaporinSynthesisReaction : IGasReactionEffect
{
    public EvaporinSynthesisReaction() { }

    public ReactionResult React(GasMixture mixture, IGasMixtureHolder? holder, AtmosphereSystem atmosphereSystem, float heatScale)
    {
        var temperature = mixture.Temperature;

        // The reaction is most efficient EXACTLY at 500K.
        // It drops off to 0 efficiency at 400K or 600K.
        var efficiency = 1.0f - (Math.Abs(temperature - 500f) / 100f);

        if (efficiency <= 0f) 
            return ReactionResult.NoReaction;

        var initialTrit = mixture.GetMoles(Gas.Tritium);
        var initialCO2 = mixture.GetMoles(Gas.CarbonDioxide);

        // Recipe: 1 part Tritium + 2 parts Carbon Dioxide
        // We limit by the scarcest reagent according to the 1:2 ratio
        var possibleByTrit = initialTrit;
        var possibleByCO2 = initialCO2 / 2f;

        var reactionMoles = Math.Min(possibleByTrit, possibleByCO2);

        if (reactionMoles <= Atmospherics.GasMinMoles)
            return ReactionResult.NoReaction;

        // Rate limit the reaction. Base is 5% (1/20) per tick at max efficiency.
        // Drops lower as temperature diverges from 500K.
        var rateLimit = (reactionMoles / 20f) * efficiency; 

        if (rateLimit <= Atmospherics.GasMinMoles)
            return ReactionResult.NoReaction;

        // Adjust reagents: -1 Trit, -2 CO2
        mixture.AdjustMoles(Gas.Tritium, -rateLimit);
        mixture.AdjustMoles(Gas.CarbonDioxide, -rateLimit * 2f);

        // Products: +1 Evaporin, +1 Ammonia (byproduct)
        mixture.AdjustMoles(Gas.Evaporin, rateLimit);
        mixture.AdjustMoles(Gas.Ammonia, rateLimit);

        return ReactionResult.Reacting;
    }
}
