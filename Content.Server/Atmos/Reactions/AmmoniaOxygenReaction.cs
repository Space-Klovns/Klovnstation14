using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Reactions;
using JetBrains.Annotations;

namespace Content.Server.Atmos.Reactions;

[UsedImplicitly]
public sealed partial class AmmoniaOxygenReaction : IGasReactionEffect
{
    public ReactionResult React(GasMixture mixture, IGasMixtureHolder? holder, AtmosphereSystem atmosphereSystem, float heatScale)
    {
        var nAmmonia = mixture.GetMoles(Gas.Ammonia);
        var nOxygen = mixture.GetMoles(Gas.Oxygen);
        var nTotal = mixture.TotalMoles;

        // KS14 start: bail out early on an empty/invalid mixture instead of dividing by zero below
        if (nTotal <= 0f || nAmmonia <= 0f || nOxygen <= 0f)
            return ReactionResult.NoReaction;
        // KS14 end

        // Concentration-dependent reaction rate
        var fAmmonia = nAmmonia / nTotal; // KS14: nAmmonia/nTotal -> nAmmonia / nTotal
        var fOxygen = nOxygen / nTotal; // KS14: nOxygen/nTotal -> nOxygen / nTotal
        var rate = MathF.Pow(fAmmonia, 2f) * MathF.Pow(fOxygen, 2f); // KS14: 2 -> 2f

        if (rate <= 0f) // KS14: added, avoid a zero-mole reaction below
            return ReactionResult.NoReaction;

        var deltaMoles = Math.Min(nAmmonia, nOxygen) / Atmospherics.AmmoniaOxygenReactionRate * 2f * rate; // KS14: nAmmonia -> Math.Min(nAmmonia, nOxygen), limit by whichever reagent runs out first

        if (deltaMoles <= 0f) // KS14: dropped the redundant nAmmonia - deltaMoles < 0 check, Math.Min above already bounds deltaMoles
            return ReactionResult.NoReaction;

        // KS14 start: release reaction heat and expose a hotspot, matching other exothermic gas reactions
        var energyReleased = 193000f * deltaMoles;
        var oldHeatCapacity = atmosphereSystem.GetHeatCapacity(mixture, true);
        var temperature = mixture.Temperature;
        var location = holder as TileAtmosphere;
        // KS14 end

        mixture.AdjustMoles(Gas.Ammonia, -deltaMoles);
        mixture.AdjustMoles(Gas.Oxygen, -deltaMoles);
        mixture.AdjustMoles(Gas.NitrousOxide, deltaMoles / 2f); // KS14: 2 -> 2f
        mixture.AdjustMoles(Gas.WaterVapor, deltaMoles * 1.5f);

        // KS14 start: apply reaction heat and expose a hotspot, matching other exothermic gas reactions
        energyReleased /= heatScale; // adjust energy to make sure speedup doesn't cause mega temperature rise
        if (energyReleased > 0f)
        {
            var newHeatCapacity = atmosphereSystem.GetHeatCapacity(mixture, true);
            if (newHeatCapacity > Atmospherics.MinimumHeatCapacity)
                mixture.Temperature = (temperature * oldHeatCapacity + energyReleased) / newHeatCapacity;
        }

        if (location != null)
        {
            var mixTemperature = mixture.Temperature;
            if (mixTemperature > 0f)
                atmosphereSystem.HotspotExpose(location, mixTemperature, mixture.Volume);
        }
        // KS14 end

        return ReactionResult.Reacting;
    }
}
