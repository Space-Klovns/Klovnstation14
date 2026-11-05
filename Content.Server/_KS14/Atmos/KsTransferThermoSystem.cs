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
    [Dependency] private KsPumpHeatSystem _pumpHeatSystem = default!;

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
    ///         (<see cref="KsCCVars.AtmosPumpCompressionHeatFraction"/>), and which recovers most of that, and is billed for
    ///         the rest, through <see cref="KsPumpHeatSystem"/>.
    /// </param>
    /// <param name="heatSink">
    ///     The surroundings themselves, when the caller already has them (a vent's tile). Takes precedence over
    ///         <paramref name="deviceUid"/>.
    /// </param>
    /// <remarks>
    ///     <para>Compression heat is never destroyed: the pump's whole work goes into the packet and its surroundings between
    ///         them, as an aftercooler would shed it, or back into the grid as electricity it isn't billed for. With nowhere
    ///         to shed it (no device, vacuum, a wall), it stays in the gas.</para>
    ///
    ///     <para><b>Recovery is a compromise: the first law is kept, and the second is given up on purpose.</b> Compressing gas
    ///         lowers its entropy, so a pump really owes its room <c>T (R ln(P2/P1) - C ln(T2/T1))</c> a mole, which is about half
    ///         its work at 3:1. Recovery on the default cvar leaves that room 4.5%, so a powered refrigeration loop can beat
    ///         Carnot.</para>
    ///
    ///     <para>Neither honest option was playable. Paying that floor in heat left a pump's room hotter than anything could
    ///         cool it, which is what #403 was about. Paying it in electricity takes kilowatts a pump, more than APCs can give
    ///         a room of them, and throttling pumps to the power they get would slow every distro on the station. So the loop's
    ///         energy adds up, and its entropy doesn't.</para>
    ///
    ///     <para>To revisit it, cap <c>recovered</c> below by that floor and retune <see cref="KsCCVars.AtmosPumpHeatRecovery"/>.
    ///         The cycle tests run with <c>NeedsPower</c> off, which recovers nothing, so they still check the real balance.</para>
    /// </remarks>
    public void Apply(GasMixture packet,
        float inPressure,
        float outPressure,
        bool doesWork,
        EntityUid? deviceUid = null,
        GasMixture? heatSink = null)
    {
        if (packet.Immutable || packet.Temperature <= 0f)
            return;

        var temperature = packet.Temperature;
        // Not only gas moles: unlinked vapor lives in the fluid, and compressing it is work all the same.
        var gasHeatCapacity = _atmosphereSystem.KsGetCompressibleHeatCapacity(packet, applyScaling: false);
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
            var unkeptWork = work * (1f - Math.Clamp(_compressionHeatFraction, 0f, 1f));

            // First, so that it doesn't depend on there being anything to shed the rest into.
            var recovered = deviceUid is { } recoveringUid ? _pumpHeatSystem.RecoverableHeat(recoveringUid, unkeptWork) : 0f;
            var shed = ShedHeat(unkeptWork - recovered, deviceUid, heatSink);
            var keptWork = work - recovered - shed;
            gasTemperature = temperature + keptWork / gasHeatCapacity;

            if (deviceUid is { } workingUid)
                _pumpHeatSystem.Record(workingUid, keptWork, shed, recovered);
        }
        else if (outPressure < inPressure)
        {
            var (mixedA, mixedB) = _gasThermoSystem.MixtureVanDerWaals(packet);
            gasTemperature = KsThrottling.ThrottledTemperature(mixedA,
                mixedB,
                gasHeatCapacity / (packet.TotalMoles + (packet.Fluid?.VaporMoles ?? 0f)),
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
