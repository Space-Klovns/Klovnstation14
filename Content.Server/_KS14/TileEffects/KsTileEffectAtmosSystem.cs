using Content.Server.Atmos.EntitySystems;
using Content.Shared._KS14.TileEffects;
using Content.Shared.Atmos;
using Content.Shared.FixedPoint;

namespace Content.Server._KS14.TileEffects;

/// <summary>
///     The atmos half of the tile effects: shared effects cannot reach a tile's air, so they ask through events.
/// </summary>
public sealed partial class KsTileEffectAtmosSystem : EntitySystem
{
    [Dependency] private AtmosphereSystem _atmosphereSystem = default!;

    private readonly List<GasMixture> _displacementTargets = [];

    [SubscribeLocalEvent]
    private void OnAirQuery(ref KsTileEffectAirQueryEvent args)
    {
        args.Mixture ??= _atmosphereSystem.GetTileMixture((args.GridUid, null, null), null, args.Tile, excite: false);
    }

    [SubscribeLocalEvent]
    private void OnBoil(ref KsTileEffectBoilEvent args)
    {
        if (args.BoiledUnits > 0f)
            return;

        args.BoiledUnits = TryChangePhase(
            args.GridUid,
            args.Tile,
            args.Gas,
            args.MaxUnits,
            args.MolesPerUnit,
            args.SensibleEnergyPerUnit,
            args.LatentEnergyPerUnitFromAir,
            args.MinimumAirTemperature,
            args.Displace);
    }

    /// <summary>
    ///     Turns up to <paramref name="maxUnits"/> of a reagent into gas in a tile's air, as many as the air can pay the
    ///         latent heat for without dropping below <paramref name="minimumTemperature"/>.
    /// </summary>
    /// <remarks>
    ///     Shared by boiling off a puddle (<see cref="Shared._KS14.TileEffects.Effects.BoilTileEffect"/>) and flashing
    ///         off a spray (<see cref="KsVaporizeTileReaction"/>), so both account energy the
    ///         same way. See <see cref="TryAddGas"/>.
    /// </remarks>
    /// <param name="sensibleEnergyPerUnit">Thermal energy each unit carries into the air, in joules.</param>
    /// <param name="latentEnergyPerUnit">Thermal energy each unit takes out of the air to change phase, in joules.</param>
    /// <param name="minimumTemperature">The air is not cooled below this, in kelvin, by the latent heat it pays.</param>
    /// <returns>Units turned to gas, floored to the 0.01u a solution stores; zero if none, or if the tile has no air.</returns>
    public float TryChangePhase(
        EntityUid gridUid,
        Vector2i tile,
        Gas gas,
        float maxUnits,
        float molesPerUnit,
        float sensibleEnergyPerUnit,
        float latentEnergyPerUnit,
        float minimumTemperature,
        bool displace)
    {
        if (maxUnits <= 0f ||
            molesPerUnit <= 0f ||
            _atmosphereSystem.GetTileMixture((gridUid, null, null), null, tile, excite: false) is not { Immutable: false } mixture)
            return 0f;

        var units = maxUnits;
        if (latentEnergyPerUnit > 0f)
        {
            // (C T + u (s - l)) / (C + u m c_gas) >= T_min, solved for the units u.
            var heatCapacity = _atmosphereSystem.GetHeatCapacity(mixture, applyScaling: true);
            var gasHeatCapacityPerUnit = molesPerUnit * _atmosphereSystem.GasMolarHeatCapacities[(int)gas];
            var costPerUnit = latentEnergyPerUnit + minimumTemperature * gasHeatCapacityPerUnit - sensibleEnergyPerUnit;
            if (costPerUnit > 0f)
                units = MathF.Min(units, heatCapacity * (mixture.Temperature - minimumTemperature) / costPerUnit);
        }

        units = (float)FixedPoint2.New(units);
        if (units <= 0f)
            return 0f;

        return TryAddGas(gridUid, tile, gas, units * molesPerUnit, units * sensibleEnergyPerUnit,
            latentEnergy: units * latentEnergyPerUnit, displace: displace)
            ? units
            : 0f;
    }

    /// <summary>
    ///     Puts something boiled into a tile's air as gas, at the ordinary one mole per molar mass in units.
    /// </summary>
    /// <inheritdoc cref="TryAddGas"/>
    /// <param name="units">Units boiled, treated as grams, as <c>ExtinguishTileReaction</c> treats them.</param>
    public bool TryAddBoiledGas(EntityUid gridUid, Vector2i tile, Gas gas, float units, float sensibleEnergy)
        => TryAddGas(gridUid, tile, gas, units / _atmosphereSystem.GasMolarMasses[(int)gas], sensibleEnergy);

    /// <summary>
    ///     Puts gas that has just changed phase (boiled off a puddle, or flashed off a spray) into a tile's air.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The gas joins the air carrying exactly <paramref name="sensibleEnergy"/>, rather than at the temperature
    ///             it boiled at: gas and reagent heat capacities differ, so the two do not agree, and only this one
    ///             conserves energy. A reagent whose gas should come out at its own temperature has its specific heat
    ///             matched to the moles it becomes.
    ///     </para>
    ///     <para>
    ///         <paramref name="latentEnergy"/> is what the change of phase took from the air: it leaves with the gas,
    ///             as <c>ExtinguishTileReaction</c> accounts it. The caller keeps it small enough that the air does not
    ///             drop below what it should.
    ///     </para>
    /// </remarks>
    /// <param name="moles">Moles of gas added.</param>
    /// <param name="sensibleEnergy">Thermal energy, in joules, the gas carries into the air.</param>
    /// <param name="latentEnergy">Thermal energy, in joules, taken out of the air to change the phase.</param>
    /// <param name="displace">
    ///     Whether the gas pushes as much of the tile's other gases out into its open neighbours, rather than adding to
    ///         them (<see cref="Displace"/>).
    /// </param>
    /// <returns>False if the tile has no mutable mixture (space) to take the gas, in which case nothing changed.</returns>
    public bool TryAddGas(EntityUid gridUid, Vector2i tile, Gas gas, float moles, float sensibleEnergy, float latentEnergy = 0f, bool displace = false)
    {
        if (moles <= 0f ||
            _atmosphereSystem.GetTileMixture((gridUid, null, null), null, tile, excite: true) is not { Immutable: false } mixture)
            return false;

        var molesBefore = mixture.TotalMoles;
        var heatCapacity = _atmosphereSystem.GetHeatCapacity(mixture, applyScaling: true);
        var energy = heatCapacity * mixture.Temperature + sensibleEnergy - latentEnergy;

        mixture.AdjustMoles(gas, moles);

        var heatCapacityAfter = _atmosphereSystem.GetHeatCapacity(mixture, applyScaling: true);
        if (heatCapacityAfter > Atmospherics.MinimumHeatCapacity)
            mixture.Temperature = MathF.Max(energy / heatCapacityAfter, Atmospherics.TCMB);

        if (displace)
            Displace(gridUid, tile, mixture, gas, molesBefore);

        return true;
    }

    /// <summary>
    ///     Pushes a share of a tile's gases, other than <paramref name="displacingGas"/>, into its open neighbours, enough
    ///         to bring its moles back down to <paramref name="targetMoles"/>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A gas released into a room does not mix into the air at once: it shoves the air that was there out of
    ///             the way. The ideal gas model atmos uses has no such thing, so this does it by hand, and atmos then
    ///             mixes the result as it would anything else. The displaced share goes evenly to the neighbours air can
    ///             flow to, carrying its own heat, so moles and energy are both conserved. A tile with no open neighbour
    ///             keeps it instead, and neither space nor the displacing gas itself is pushed anywhere.
    ///     </para>
    ///     <para>
    ///         It goes by moles, not pressure: a cold gas lowers the pressure as it cools the tile, which would leave
    ///             nothing to push. Air flows back into the cold tile afterwards, as atmos would have it, mixed.
    ///     </para>
    /// </remarks>
    private void Displace(EntityUid gridUid, Vector2i tile, GasMixture mixture, Gas displacingGas, float targetMoles)
    {
        var excessMoles = mixture.TotalMoles - targetMoles;
        if (excessMoles <= 0f)
            return;

        var otherMoles = mixture.TotalMoles - mixture.GetMoles(displacingGas);
        if (otherMoles <= Atmospherics.GasMinMoles)
            return;

        _displacementTargets.Clear();
        var enumerator = _atmosphereSystem.GetAdjacentTileMixtures((gridUid, null), tile, excite: true);
        while (enumerator.MoveNext(out var neighbourMixture))
        {
            if (!neighbourMixture.Immutable)
                _displacementTargets.Add(neighbourMixture);
        }

        if (_displacementTargets.Count == 0)
            return;

        var fraction = MathF.Min(excessMoles / otherMoles, 1f);

        var displaced = new GasMixture(mixture.Volume) { Temperature = mixture.Temperature };
        for (var gasIndex = 0; gasIndex < Atmospherics.TotalNumberOfGases; gasIndex++)
        {
            if (gasIndex == (int)displacingGas)
                continue;

            var moles = mixture.GetMoles(gasIndex) * fraction;
            if (moles <= 0f)
                continue;

            mixture.AdjustMoles(gasIndex, -moles);
            displaced.AdjustMoles(gasIndex, moles);
        }

        for (var i = 0; i < _displacementTargets.Count; i++)
        {
            // An even share of what is left: the last neighbour takes the remainder.
            var share = displaced.RemoveRatio(1f / (_displacementTargets.Count - i));
            _atmosphereSystem.Merge(_displacementTargets[i], share);
        }

        _displacementTargets.Clear();
    }
}
