using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using JetBrains.Annotations;
using Robust.Shared.Map;

namespace Content.Server.Chemistry.TileReactions
{
    [UsedImplicitly]
    [DataDefinition]
    public sealed partial class ExtinguishTileReaction : ITileReaction
    {
        [DataField("coolingTemperature")] private float _coolingTemperature = 2f;

        // KS14 start: energy-conserving extinguishing
        /// <summary>
        ///     Effective energy absorbed per reagent unit boiled off, in the scaled energy units atmos uses
        ///         (<c>heat capacity (scaled) * temperature</c>). Deliberately far above physical: it is tuned so
        ///         that the ~1.67u an extinguisher vapor passes per tile still one-shots fires up to ~2000 K.
        /// </summary>
        [DataField]
        public float EnergyPerUnit = 250000f;

        /// <summary>
        ///     Cooling floor for a reagent that has a gas but no <see cref="ReagentPrototype.BoilingPoint"/>.
        ///         The floor is normally the reagent's boiling point (converted from Celsius).
        /// </summary>
        private const float FallbackMinimumTemperature = Atmospherics.FireMinimumTemperatureToExist;

        /// <summary>
        ///     The gas a reagent boils into: the one whose <see cref="Content.Shared.Atmos.Prototypes.GasPrototype.Reagent"/>
        ///         is this reagent. <c>null</c> when there is none. Reads <c>GasReagents</c>, which atmos already
        ///         builds once from the gas prototypes, so there is nothing to cache here.
        /// </summary>
        public static Gas? GetBoiledGas(AtmosphereSystem atmosphereSystem, ReagentPrototype reagent)
        {
            var gasReagents = atmosphereSystem.GasReagents;

            for (var i = 0; i < gasReagents.Length; i++)
            {
                if (gasReagents[i] == reagent.ID)
                    return (Gas)i;
            }

            return null;
        }
        // KS14 end

        public FixedPoint2 TileReact(TileRef tile,
            ReagentPrototype reagent,
            FixedPoint2 reactVolume,
            IEntityManager entityManager,
            List<ReagentData>? data)
        {
            if (reactVolume <= FixedPoint2.Zero || tile.Tile.IsEmpty)
                return FixedPoint2.Zero;

            var atmosphereSystem = entityManager.System<AtmosphereSystem>();

            var environment = atmosphereSystem.GetTileMixture(tile.GridUid, null, tile.GridIndices, true);

            if (environment == null || !atmosphereSystem.IsHotspotActive(tile.GridUid, tile.GridIndices))
                return FixedPoint2.Zero;

            // KS14 start: water boils off and absorbs latent heat instead of destroying energy
            /*
            environment.Temperature =
                MathF.Max(MathF.Min(environment.Temperature - (_coolingTemperature * 1000f),
                        environment.Temperature / _coolingTemperature), Atmospherics.TCMB);
            */
            var usedUnits = FixedPoint2.Zero;

            // Only reagents that have a gas boil off and cool; the rest (drinks, holy water...) just put the fire out.
            var boiledGas = GetBoiledGas(atmosphereSystem, reagent);

            if (boiledGas != null && !environment.Immutable)
            {
                var temperature = environment.Temperature;
                var floorTemperature = reagent.BoilingPoint is { } boilingPoint
                    ? boilingPoint /* KS14: + Atmospherics.T0C removed, BoilingPoint is in Kelvin */
                    : FallbackMinimumTemperature;
                var target = MathF.Max(
                    MathF.Min(temperature - (_coolingTemperature * 1000f), temperature / _coolingTemperature),
                    floorTemperature);

                var heatCapacity = atmosphereSystem.GetHeatCapacity(environment, applyScaling: true);

                if (target < temperature && heatCapacity > Atmospherics.MinimumHeatCapacity && EnergyPerUnit > 0f)
                {
                    var energyNeeded = heatCapacity * (temperature - target);
                    // Quantise first, so the energy absorbed matches exactly the water reported as consumed.
                    usedUnits = FixedPoint2.Min(reactVolume, FixedPoint2.New(energyNeeded / EnergyPerUnit));
                    var used = (float) usedUnits;
                    var energy = used * EnergyPerUnit;

                    // C_before*T = C_after*T' + E, with the boiled water joining the air as vapor.
                    // Moles first, then read C_after from the mixture itself, so the vapor is scaled exactly as the
                    //     rest of the mixture is (no separate per-gas heat capacity lookup to keep in step).
                    // 1u is treated as 1g, so moles = grams / molar mass.
                    var vaporMoles = used / atmosphereSystem.GasMolarMasses[(int)boiledGas.Value];
                    environment.AdjustMoles(boiledGas.Value, vaporMoles);

                    var heatCapacityAfter = atmosphereSystem.GetHeatCapacity(environment, applyScaling: true);
                    var newTemperature = (heatCapacity * temperature - energy) / heatCapacityAfter;
                    environment.Temperature = MathF.Max(newTemperature, Atmospherics.TCMB);
                }
            }
            // KS14 end

            atmosphereSystem.ReactTile(tile.GridUid, tile.GridIndices);
            atmosphereSystem.HotspotExtinguish(tile.GridUid, tile.GridIndices);

            // KS14 start: report consumed water; ReagentPrototype.ReactionTile permits anything <= reactVolume
            return usedUnits;
            // KS14 end
        }
    }
}
