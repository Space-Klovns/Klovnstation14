using Content.Server.Atmos.EntitySystems;
using Content.Shared._KS14.Atmos;
using Content.Shared._KS14.CCVar;
using Content.Shared.Atmos;
using Robust.Shared.Configuration;

namespace Content.Server._KS14.Atmos;

/// <summary>
///     The thermodynamics of a packet of gas crossing a device: a pump compressing it uphill heats it adiabatically, and
///         gas throttled downhill cools or warms by Joule–Thomson. Applied to the moved packet before it merges, so the
///         merge's heat-capacity mix spreads the change into the output.
/// </summary>
/// <remarks>
///     <para>Pressure stays ideal-gas everywhere; this only moves temperature. Atmos stores <c>E = C T</c>, so the
///         Joule–Thomson change appears as energy made or lost: the real-gas internal energy atmos can't represent. No
///         free cycle comes of it, since compression heat over the same pressure ratio is far larger.</para>
///
///     <para>Not applied to tile-to-tile flow, where pressure differences are small and the path is the hottest in atmos.</para>
/// </remarks>
public sealed partial class KsTransferThermoSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private AtmosphereSystem _atmosphereSystem = default!;
    [Dependency] private KsGasThermoSystem _gasThermoSystem = default!;

    private float _pumpEfficiency;
    private float _compressionHeatFraction;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_configurationManager, KsCCVars.AtmosPumpEfficiency, value => _pumpEfficiency = value, true);
        Subs.CVar(_configurationManager, KsCCVars.AtmosPumpCompressionHeatFraction, value => _compressionHeatFraction = value, true);
    }

    /// <summary>
    ///     Heats or cools <paramref name="packet"/>, moved from a mixture at <paramref name="inPressure"/> into one at
    ///         <paramref name="outPressure"/> kPa.
    /// </summary>
    /// <param name="doesWork">
    ///     Whether the device forces the flow (a pump). Uphill, a pump compresses the packet; downhill, and through any
    ///         passive device, the packet throttles.
    /// </param>
    /// <param name="deviceUid">
    ///     The pump, whose surroundings take the share of its compression heat that the gas doesn't
    ///         (<see cref="KsCCVars.AtmosPumpCompressionHeatFraction"/>). Looked up only when there is such a share.
    /// </param>
    /// <param name="heatSink">
    ///     The surroundings themselves, when the caller already has them (a vent's tile). Takes precedence over
    ///         <paramref name="deviceUid"/>.
    /// </param>
    /// <remarks>
    ///     Compression heat is never destroyed: the pump's whole work goes into the packet and its surroundings between
    ///         them, as an aftercooler would shed it. Destroying part of it would let a refrigeration loop cool for less
    ///         work than the second law allows. With nowhere to shed it (no device, vacuum, a wall), it all stays in the gas.
    /// </remarks>
    public void Apply(GasMixture packet,
        float inPressure,
        float outPressure,
        bool doesWork,
        EntityUid? deviceUid = null,
        GasMixture? heatSink = null)
    {
        if (packet.Immutable || packet.TotalMoles <= 0f || packet.Temperature <= 0f)
            return;

        var temperature = packet.Temperature;
        var gasHeatCapacity = _atmosphereSystem.KsGetGasHeatCapacity(packet, applyScaling: false);
        if (gasHeatCapacity <= 0f)
            return;

        float gasTemperature;
        if (outPressure > inPressure)
        {
            if (!doesWork)
                return;

            var heatCapacityRatio = _gasThermoSystem.MixtureHeatCapacityRatio(packet);
            var compressed = KsThrottling.CompressedTemperature(heatCapacityRatio, _pumpEfficiency, temperature, inPressure, outPressure);
            var work = gasHeatCapacity * (compressed - temperature);
            var keptWork = work - ShedHeat(work * (1f - Math.Clamp(_compressionHeatFraction, 0f, 1f)), deviceUid, heatSink);
            gasTemperature = temperature + keptWork / gasHeatCapacity;
        }
        else if (outPressure < inPressure)
        {
            var (mixedA, mixedB) = _gasThermoSystem.MixtureVanDerWaals(packet);
            gasTemperature = KsThrottling.ThrottledTemperature(mixedA,
                mixedB,
                gasHeatCapacity / packet.TotalMoles,
                temperature,
                inPressure,
                outPressure);
        }
        else
        {
            return;
        }

        // The work is done on the gas; any liquid riding in the packet shares the heat.
        var totalHeatCapacity = _atmosphereSystem.GetHeatCapacity(packet, applyScaling: false);
        packet.Temperature = MathF.Max(temperature + (gasTemperature - temperature) * gasHeatCapacity / totalHeatCapacity,
            Atmospherics.TCMB);
    }

    /// <summary>
    ///     Puts up to <paramref name="heat"/> joules into the surroundings.
    /// </summary>
    /// <returns>The joules actually shed: zero if there is nothing to shed them into.</returns>
    private float ShedHeat(float heat, EntityUid? deviceUid, GasMixture? heatSink)
    {
        if (heat <= 0f)
            return 0f;

        heatSink ??= deviceUid is { } uid ? _atmosphereSystem.GetContainingMixture(uid, excite: true) : null;
        // Vacuum takes none. Space reports a heat capacity even empty, so moles decide it.
        if (heatSink == null || heatSink.TotalMoles <= 0f)
            return 0f;

        // Atmos's own energy is scaled, so the unscaled work goes in over the unscaled heat capacity.
        var sinkHeatCapacity = _atmosphereSystem.GetHeatCapacity(heatSink, applyScaling: false);
        if (sinkHeatCapacity <= 0f)
            return 0f;

        // An immutable mixture with air in it (a planet's atmosphere) is a bath that takes any heat unchanged, as it does a
        //      radiator's.
        if (!heatSink.Immutable)
            heatSink.Temperature += heat / sinkHeatCapacity;
        return heat;
    }
}
