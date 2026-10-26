using System.Linq;
using Content.Shared.Atmos;

namespace Content.Shared._KS14.Procedural;

public sealed record KsProcgenGasAmount(string GasId, float Moles);

/// <summary>Detached tile initialization proposal. Does not own or mutate an engine gas mixture.</summary>
public sealed record KsProcgenAtmosphereMixture(float VolumeLiters, float TemperatureKelvin,
    float RequestedPressureKPa, float CalculatedPressureKPa, IReadOnlyList<KsProcgenGasAmount> Gases)
{
    public bool InitialGasVerified => false;
    public bool AtmosphereAtRestVerified => false;
}

public static class KsProcgenAtmosphereMixturePlanner
{
    /// <summary>Use the actual tile mixture's volume, not the total room volume, for this proposal.</summary>
    public static bool TryPlan(KsProcgenAtmosphereProfile profile, float volumeLiters,
        out KsProcgenAtmosphereMixture? mixture, out KsProcgenIssue? issue)
    {
        mixture = null;
        issue = null;
        if (profile == null || !float.IsFinite(volumeLiters) || volumeLiters <= 0f)
        {
            issue = new("InvalidAtmosphereVolume", "A validated atmosphere profile and positive finite tile volume are required.");
            return false;
        }
        if (profile.Mode == KsProcgenAtmosphereMode.Preserve)
        {
            issue = new("PreservedAtmosphereNotWritable", "Preserved gas does not have an initialization proposal.");
            return false;
        }
        var temperature = profile.TemperatureKelvin!.Value;
        var pressure = profile.PressureKPa!.Value;
        var totalMoles = (double) pressure * (double) volumeLiters / ((double) Atmospherics.R * (double) temperature);
        // The engine adds float gas quantities; reject totals that its own arithmetic cannot represent.
        if (!double.IsFinite(totalMoles) || totalMoles > (double) float.MaxValue)
        {
            issue = new("AtmosphereMixtureOverflow", "The requested tile mixture exceeds engine numeric limits.");
            return false;
        }
        var gases = new List<KsProcgenGasAmount>();
        var engineMixture = new GasMixture(volume: volumeLiters) { Temperature = temperature };
        foreach (var fraction in profile.MoleFractions)
        {
            var moles = (float) (totalMoles * (double) fraction.Fraction);
            if (!float.IsFinite(moles) || moles <= 0f || !KsProcgenAtmosphereProfiles.TryGas(fraction.GasId, out var gas))
            {
                issue = new("AtmosphereMixturePrecisionLoss", "A requested positive gas quantity cannot be represented by the engine.");
                return false;
            }
            engineMixture.SetMoles(gas, moles);
            gases.Add(new(fraction.GasId, moles));
        }
        var actualPressure = engineMixture.Pressure;
        if (!float.IsFinite(engineMixture.TotalMoles) || !float.IsFinite(actualPressure) ||
            Math.Abs((double) actualPressure - (double) pressure) > (double) profile.PressureToleranceKPa)
        {
            issue = new("AtmosphereMixturePrecisionLoss", "Engine pressure arithmetic does not meet the requested tolerance.");
            return false;
        }
        foreach (var gas in gases)
        {
            var actualFraction = gas.Moles / engineMixture.TotalMoles;
            var requestedFraction = profile.MoleFractions.First(fraction => fraction.GasId == gas.GasId).Fraction;
            if (Math.Abs(actualFraction - requestedFraction) <= profile.FractionTolerance)
                continue;
            issue = new("AtmosphereMixturePrecisionLoss", "Engine mole fractions do not meet the requested tolerance.");
            return false;
        }
        mixture = new(volumeLiters, temperature, pressure, actualPressure, KsProcgenReadOnly.Copy(gases));
        return true;
    }
}
