# Fluidmos: reagents inside atmospherics

This document is for maintainers. It explains how chemistry reagents ride inside atmos mixtures as solid, liquid and
vapor, where each part of that lives in the code, and what holds it together. What the player sees is described in
three guidebook pages, which are the best summary of the intended behaviour. Read them first:

- `Resources/ServerInfo/_KS14/Guidebook/Engineering/LiquidsInPipes.xml`
- `Resources/ServerInfo/_KS14/Guidebook/Engineering/FluidDevices.xml`
- `Resources/ServerInfo/_KS14/Guidebook/Engineering/Refrigeration.xml`

Each section below starts from a guidebook claim and says what makes it true. If you change a mechanic, change the
guidebook page (both `en-US` and `ru-RU`) in the same commit.

## The idea in one paragraph

Upstream pipes carry only gas. Fluidmos lets a `GasMixture` carry reagents too, in a `KsFluidMixture` held on the
mixture. The reagents move with the gas through every existing device. They boil and condense against a real
vapor-pressure curve, freeze and melt at their melting point, and take or give heat when they do. When they reach open
air they leave as puddles (liquid), ice sheets (solid), their linked gas (vapor of a linked reagent) or smoke (vapor of
an unlinked one). **Tile air never holds fluid.** Puddles are the tile's liquid.

## Design rules

These decide most "why is it like this" questions. Keep them when changing anything.

- **Tiles pay nothing, dry pipes pay nothing.** `GasMixture.Fluid` is null unless the mixture holds reagents, and a
  dry mixture costs one null check. Tile mixtures can never hold fluid at all (below).
- **No prototype lookups on the hot path.** Reagent properties are resolved once into a table
  (`KsFluidmosPropertiesSystem`), and every constant is a `const` in `KsFluidmos.cs`, not a cvar.
- **One explicit step per update.** The phase step never iterates or recurses within a tick. Every rate is capped so
  it can't overshoot equilibrium, and a single step's latent heat may move the temperature by at most
  `KsFluidmos.MaxPhaseDeltaT`.
- **Energy is conserved.** Every phase change pays or returns its latent heat, and the temperature after a step is
  recomputed from energy, because a phase change also changes the heat capacity.
- **Matter is conserved.** Nothing is lost to rounding. Where a float amount has to become a `FixedPoint2` solution,
  what falls below 0.01u is carried forward (`KsFluidmosTileBufferComponent.Remainder`), not dropped.
- **Game scale, not SI**, except where a real number is load-bearing (below).

## Data model

| Type | File | What it is |
| --- | --- | --- |
| `KsFluidMixture` | `Content.Shared/_KS14/Fluidmos/KsFluidMixture.cs` | The reagents a mixture carries, keyed by `KsFluidReagentKey`. Caches its heat capacity and phase-step state. |
| `KsFluidAmount` | `KsFluidAmount.cs` | One reagent's `Solid` (u), `Liquid` (u) and `Vapor` (mol). |
| `KsFluidReagentKey` | `KsFluidReagentKey.cs` | Prototype id plus reagent data, so blood keeps its DNA through a pipe. |
| `GasMixture` partial | `Content.Shared/Atmos/GasMixture.Klovn.Fluid.cs` | `Fluid`, `FluidCapable`, `FreeVolume`, `TransferMoles`, the fluid-aware pressure. |
| `KsFluidmosReagentData` | `KsFluidmosReagentData.cs` | The optional `fluidmos:` block on a reagent prototype. |
| `KsFluidReagentProperties` | `KsFluidReagentProperties.cs` | Resolved per-reagent numbers, built by `KsFluidmosPropertiesSystem`. |
| `KsPhaseMath` | `KsPhaseMath.cs` | Saturation pressure, boiling point at pressure, Hertz–Knudsen flux, moles per unit, default latent heats. |

### Which mixtures hold fluid

`GasMixture.FluidCapable` is **opt-in**, because tile mixtures are reassigned in too many places for an opt-out to be
safe. It is set by the owner and is not saved, so the owner sets it again on every load:

- pipe nets, in `PipeNet.cs` (a `KS14 start` block);
- canisters, portable scrubbers and gas tanks, in `KsFluidmosCapabilitySystem`.

Packets split off a capable mixture inherit the flag, and so does `GasMixture.CopyFrom`. Immutable mixtures never hold
fluid. `SharedAtmosphereSystem.Klovn.Fluidmos.cs` has the fluid half of `Merge`: merging fluid into a mixture that
can't hold it asserts in Debug, logs, and drops the fluid. Reaching that means some route from a device to a tile
didn't detach the fluid first, which is a bug.

### Linked and unlinked reagents

The guidebook says liquid water boils into water vapor, but ethanol boils into "a vapor that only exists in the pipe".
That is the link:

- **Linked** reagents name an atmos gas (`fluidmos: gas: WaterVapor`). Their vapor *is* that gas's moles in the
  `GasMixture`, so `KsFluidAmount.Vapor` stays 0 for them. Their vapor spreads across tiles as gas, shows in the gas
  table, and condenses back in pipes. Their molar mass comes from the gas.
  Currently linked: Water, Oxygen, Nitrogen, NitrousOxide, Ammonia, CarbonDioxide.
- **Unlinked** reagents keep their vapor in `KsFluidAmount.Vapor` (moles). It leaves a pipe as smoke.

**Plasma must stay unlinked.** With its real boiling point (about 352 K), a linked plasma gas would condense at room
temperature, and every plasma canister and line on the station would turn to liquid. Unlinked, the reagent behaves as
a liquid fuel in pipes while the gas is untouched. Tritium and Frezon are unlinked too.

### Volume, pressure and full pipes

> Liquid takes up room, so it squeezes the gas sharing the pipe. A pipe that fills right up with liquid [...] its
> pressure shoots up.

- 1u of liquid or solid displaces `LitersPerUnit` (5 mL). `FreeVolume` is what's left to the gas, never below
  `MinFreeVolumeFraction` (2%) of the volume.
- Pressure is gas and unlinked vapor over the free volume, plus a hydraulic term of `HydraulicStiffness` kPa per percent
  overfilled past that floor, capped at `MaxHydraulicPressure`. The cap exists because overfill is unbounded (a deleted
  pipe shrinks its net, a merge never checks capacity), and an infinite pressure becomes NaN in the transfer helpers.
- `TransferMoles` is what a transfer divides by: gas moles, plus unlinked vapor, plus liquid at
  `LiquidPseudoMolesPerUnit` moles per unit. `TotalMoles` stays gas-only, so gas fractions keep their meaning.
- **Solids never move with a transfer.** `RemoveRatio` leaves them behind, which is why "ice in a pipe stays put" and
  pumping from a slurry takes only the liquid. `DivideInto` (pipe net splits) is the exception: the solids are
  physically in those pipes, so each piece gets its volume's share.

## The physics

### One vapor-pressure curve

Every reagent uses Clausius–Clapeyron, anchored at its 1 atm boiling point:

```
Psat(T) = 101.325 kPa * exp(k * (1 - Tb / T))        k = ΔS_vap / R
```

`BoilingPointAt(P)` is its inverse, and is what the guidebook means by "the lower the pressure, the colder it boils".

**`k` and the latent heat are separate numbers. Never derive one from the other.** `k` is a real, per-mole quantity:
Trouton's rule (`DefaultVaporizationEntropy`, 10.6) by default, overridden per reagent by `vaporizationEntropy` for the
known outliers (water 13.1). The latent and fusion heats are game-scale energies per unit (`LatentHeatRatio`,
calibrated to water's 540 J/u from `BoilTileEffect`, and `FusionHeatRatio`, Richards' rule). Deriving `k` from the
game-scale latent heat gives water `k ≈ 3.1` and `Psat(293 K)` near 43 kPa instead of about 2.3, so rooms would hold
twenty times too much humidity.

Every reagent has `SpecificHeat` 1 J/(K·u). Moles per unit treat 1u as 1 g (`GramsPerUnit`), the convention
`BoilTileEffect` already used, so pipes and puddles turn the same reagent into the same amount of gas. All reagent
temperatures in YAML are kelvin.

### The phase step

`KsFluidmosPhaseSystem.UpdatePhases` runs from `AtmosphereSystem.React`, gated on `FluidCapable`, so every pipe net,
canister and tank gets one step whenever atmos reacts it. For each reagent:

- **Below its boiling point at the mixture's pressure**, it evaporates or condenses at the Hertz–Knudsen rate toward
  saturation, over an interface area of `PipeInterfaceAreaPerLiter`, with `HertzKnudsenAlpha`. This is "a closed pipe
  of water at room temperature keeps its water": it stops once the vapor is saturated.
- **Above it, liquid flashes**, limited only by the heat available, down to that boiling point. That is "a liquid
  warmer than its boiling point boils at once [...] and the pipe settles at the boiling point", and it's what the
  expansion valve relies on.
- **Freezing and melting are energy-limited plateaus at the melting point**, not a statistical fraction. Below its
  melting point a pure substance freezes completely, as fast as its fusion heat can leave. (A Boltzmann "fraction
  frozen" would wrongly put ice in 10 °C water.)
- **A solid with no liquid of its own sublimes.**
- **Below the triple point** (`IsBelowTriplePoint`: the boiling point at the mixture's pressure is under the melting
  point) there is no stable liquid. Solid and vapor convert directly, and any liquid freezes one `MaxPhaseDeltaT` step
  at a time. CO2 at one atmosphere is the case that needs this: dry ice sublimes rather than melting.
- **Linked gases never condense at or above their critical temperature** ("too hot and it can't be liquid at all").
  Tc = 8a / (27 R b), from each gas's van der Waals constants in `Partials/gases.yml`.

A mixture whose last step moved almost nothing is marked settled, and skipped until its temperature, pressure or
composition changes. A dry mixture only wakes if a linked gas in it is past saturation, so steam pumped into a cold
pipe can condense.

### No supercritical fluids

Above Tc everything is ideal gas. There is no dense single phase. Unlinked reagents have no Tc. Their curve is only
bounded by the clamp on the exponent in `SaturationPressure`. This is a deliberate scope limit, not an oversight.

### Pumps heat, valves chill

`KsTransferThermoSystem` (`Content.Server/_KS14/Atmos/`) is applied to a moved packet before it merges:

- **Compression**: a pump pushing uphill heats the packet adiabatically, with an efficiency
  (`KsCCVars.AtmosPumpEfficiency`). The packet keeps only `AtmosPumpCompressionHeatFraction` (default 0.1) of that
  heat. The rest goes to the pump's room, or to an immutable heat sink with moles (planet air, lattice). A pump in
  vacuum keeps it all.
- **Throttling**: gas let downhill changes temperature by Joule–Thomson (`KsThrottling`, Shared). It uses the van der
  Waals constants only as a thermal correction. A gas without them is ideal and doesn't change. Tritium's inversion
  temperature is low enough that it warms.

This applies to all of atmos, not only fluid-carrying mixtures.

## Moving liquid through devices

> Pumps push liquid along with the gas. Passive valves, regulators and pressure-controlled valves let liquid through
> by the pressure difference across them, separately from the gas.

- **Pumps, filters, mixers** move liquid as part of the packet, through `TransferMoles`.
- **Passive devices** (passive gate, regulator, pressure-controlled valve, passive vent) move liquid separately, by the
  orifice law, in `KsLiquidFlowSystem.Transfer`: `conductance * √ΔP * dt` units, bisected so the flow can't overshoot
  the pressures it equalises. `DefaultLiquidConductance` gives 100 u/s at 100 kPa. The regulator passes
  `minimumInletPressure`, so it never drains its inlet below its threshold.
- **Radiators** (`HeatExchangerSystem.Klovn.Fluidmos.cs`) take their packet by the gas share, sweeping the same share
  of liquid. Taking it by `TransferMoles` choked liquid-laden nets. They condense and boil the passing packet at line
  pressure (`KsFluidmosPhaseSystem.ExchangeAtWall`), with the latent heat going to their tile, bounded by
  `KsLatentHeatTransferScale` (1/64) of their heat transfer. Unbounded, a condenser condensed at exactly ambient, and
  the chain's pressure drop re-boiled its liquid before the valve.

Every upstream device that touches this carries a `KS14` marker. To find them all, grep for `.Fluid`, `FluidCapable`,
`KsLiquidFlowSystem` and `KsFluidmosTileSystem` outside `_KS14/Fluidmos/`.

### The fluid devices

All are in `Content.Server/_KS14/Fluidmos/Devices/`, with prototypes in `Entities/devices.yml`. Each can be built with
the RPD and the pipe layer.

- **Fluid port** (`KsFluidPortSystem`): the only place pipes meet plumbing. Injects its tank's solution into the pipe
  as liquid, or, switched to extract, draws the pipe's liquid into its tank. Liquid only: ice and gas stay. While
  extracting it refuses plumbing input, so a plumbing loop can't feed it its own output.
- **Phase separator** (`KsPhaseSeparatorSystem`): pumps gas and vapor through and liquid out of its side. It needs
  power (`ApcPowerReceiver`, 200 W) and has no pressure limit beyond that. Put a heater ahead of it and it is a still:
  the vapor over a warm mixture is enriched in whatever is most volatile (Raoult's law).
- **Expansion valve** (`KsExpansionValveSystem`, UI in `Content.Client/_KS14/Fluidmos/Devices/`): passes liquid to a
  lower pressure, where the flash in the next phase step chills it to the boiling point there. With no liquid at its
  inlet it passes vapor, so an empty loop still starts. "Hold outlet pressure" opens and closes it to keep the outlet
  at a set pressure. Since the outlet pressure sets the boiling point, that sets the evaporator's temperature.

## Out of the pipe: the device–tile interface

> Whatever leaves a pipe for the room keeps its phase.

Every route from a pipe to a tile goes through `KsFluidmosTileSystem`. **Nothing else may hand fluid to a tile
mixture.**

- **`ReleaseToTile(deviceUid, released)`** detaches `released.Fluid`, merges the gas into the tile as usual, and
  splits the fluid:
  - linked vapor is already gas, so it goes with the gas;
  - liquid goes into the device's `KsFluidmosTileBufferComponent` and is spilled as a puddle once there's
    `SpillThresholdUnits` of it;
  - unlinked vapor is buffered and released as smoke past `SmokeThresholdUnits`;
  - solid becomes an ice sheet;
  - the sub-0.01u remainder is carried in `Remainder` until the next release.

  Buffers are flushed when the device is deleted or the component is removed.
- **`AbsorbFromTile`** is the reverse, for siphoning vents (draining the tile's puddle at `PuddleDrainRate`) and
  scrubbers (taking in smoke).
- **`QueueSpill` / `QueueCondensate`** defer puddle work out of atmos callbacks to the system's `Update`.

**Spills on destruction.** A pipe broken or destroyed by damage spills its share of the net's fluid once
(`OnNodeContainerDestroyed`, on `DestructionEventArgs`). A pipe that is merely *deleted* (an admin, a deconstruct)
spills nothing, the same as gas. A broken canister or tank spills what it held.

## Puddles and ice: the tile's fluid

Tile air holds only gas. Liquid on a tile is a puddle and solid is an ice sheet. Both exchange with the air through tile
effects and reactions, not through `KsFluidMixture`.

| Guidebook | Mechanism | File |
| --- | --- | --- |
| Water puddles slowly dry into humidity, slower in humid rooms | Hertz–Knudsen at `PuddleHertzKnudsenAlpha`, calibrated to upstream's evaporation rate on a dry tile, toward the tile's partial pressure. Linked reagents skip upstream evaporation. | `SharedPuddleSystem.Evaporation.cs` (KS14 block) |
| In vacuum they boil away | `BoilTileEffect`: boil above the boiling point at the tile's partial pressure, evaporate below it, capped at the dew point | `Content.Shared/_KS14/TileEffects/Effects/BoilTileEffect.cs` |
| Humid air on a cold floor condenses into a puddle | `KsCondensationReaction`, a gas reaction per linked gas, past saturation by `SupersaturationMargin` so puddle and air don't trade the same vapor; `maximumTemperature` is Tc | `Content.Server/_KS14/Fluidmos/KsCondensationReaction.cs`, `reactions.yml` |
| A puddle on a freezing floor turns into ice | `FreezeTileEffect`, an energy-limited plateau, into one `KsIceSheet` per tile | `FreezeTileEffect.cs` |
| Ice melts back when warm; in vacuum it sublimes without melting | `KsIceSheetSystem`: exchanges heat with its air and its puddle, melts into the puddle, sublimes into the air capped at the frost point | `Content.Server/_KS14/Fluidmos/Ice/KsIceSheetSystem.cs` |
| Cold liquid boils off into its gas | `KsVaporizeTileReaction` (sprays, e.g. the CO2 extinguisher) and `BoilTileEffect` | `Content.Server/_KS14/TileEffects/KsVaporizeTileReaction.cs` |

Puddles also conduct heat to their neighbours and to the air (`PuddleSystem.Klovn.Conduction.cs`,
`PuddleSystem.Klovn.AirHeatExchange.cs`). Ice sheets are cached per tile like puddles.

**Simultaneous exchanges split their capacity.** Puddle–neighbour, puddle–air and ice–air/puddle steps all start from
the same temperatures and are applied together. Each one may only work with its share of a body's heat capacity, which
is the capacity divided by the number of contacts the body actually has (`CountHeatContacts`; the air counts only if
the grid has air and no foam blanket covers the tile). If each step used the whole capacity, a small hot puddle between
cold neighbours in cold air would be carried past all of them, below absolute zero. That was a real bug, found as a
`HeatContainer` debug assert at about −132 K.

**Heat units.** Tile–puddle exchanges use the air's *scaled* heat capacity against the solution's joules. That is the
established convention throughout the puddle code, not a bug.

## The UI

- **Gas analyzer** (`GasAnalyzerSystem`, `GasAnalyzerWindow.xaml.cs`) lists reagents under the gases in
  `KsFluidListControl` (`Content.Client/_KS14/Fluidmos/`). It is a grid with liquid, solid and vapor columns. Linked
  reagents have no vapor cell, because their vapor is in the gas table, and the column is hidden entirely when every
  reagent is linked. Each row has a bar showing that reagent's share of the total. The data is `KsFluidEntry`.
- **Canister window** shows its liquid as a band under the gas (`GasCanisterBoundUserInterface`,
  `GasCanisterOverlayDataSystem`).

## Adding a reagent

1. Give the reagent a `boilingPoint` and `meltingPoint` in kelvin.
2. Add a `!PartialOnly reagent` entry to `Resources/Prototypes/_KS14/Fluidmos/Partials/reagents.yml` with a `fluidmos:`
   block. Every field is optional:
   - `vaporizationEntropy` (real ΔH_vap / (R Tb), default 10.6);
   - `molarMass` (g/mol, default 100, ignored when linked);
   - `latentHeat` and `fusionHeat` (J/u, default from the ratios);
   - `molesPerUnit`;
   - `gas`, to link it.
3. To link it to a gas:
   - Add the gas's van der Waals `ksVanDerWaalsA`/`ksVanDerWaalsB` in `Partials/gases.yml`, or it is ideal and has no
     Tc.
   - Add a `KsCondensationReaction` gas reaction in `reactions.yml`, with `maximumTemperature` set to 8a / (27 R b).
     `KsCondensationTests` checks every linked gas has one and that it matches.
   - Give the reagent tile effects so its puddles boil and evaporate into the gas (`BoilTileEffect`), and freeze if it
     can (`FreezeTileEffect`).
   - Check every prototype that holds the gas. A canister of a gas that condenses at room temperature now arrives as
     liquid, or as solid for CO2. Fill it with the phase it should hold (`Partials/canisters.yml`), or
     `KsCanisterFillTests` fails on the drift.
4. Before linking, ask whether the gas condenses at station conditions. If it does and the station relies on it as a
   gas, don't link it (see plasma, above).

## Testing

Tests live in `Content.IntegrationTests/Tests/_KS14/Fluidmos/`:

| File | Covers |
| --- | --- |
| `KsPhaseMathTests`, `KsFluidMixtureTests` | The curve and the data type |
| `KsFluidmosPhaseTests` | Boiling, condensing, freezing, flash, triple point, critical temperature |
| `KsCondensationTests`, `KsPuddlePhaseTests`, `KsIceSheetTests` | The tile side |
| `KsFluidmosDeviceTests`, `KsFluidmosNewDeviceTests`, `KsFluidPortPlumbingTests` | Device transfers, spills, the three devices |
| `KsRadiatorFluidTests` | Radiator sweep and wall exchange |
| `KsFluidmosCycleTests` | Whole refrigeration loops: ammonia, N2O in space, a Linde nitrogen liquefier, the loop's energy balance |
| `KsCanisterFillTests`, `KsFluidmosRpdTests`, `KsFluidListControlTests` | Canister fills, RPD, the analyzer UI |

The cycle tests are what the Refrigeration guidebook page is built from. If one starts failing after a balance pass,
the page probably needs updating too. Each loop uses a **volume pump** as the compressor: a pressure pump has no
displacement limit and drains the suction to a few kPa.

The usual repo rules apply, and they've all mattered here:
- Prove each test red.
- Run in Debug, because the `HeatContainer` temperature assert is how negative-kelvin bugs show up.
- Read the trx, since a failed `GameTest` in a batch can show as `NotExecuted`.

Phase 6 benchmarks (`KsAtmosStationBenchmark`, `GasReactionBenchmark`, `HeatCapacityBenchmark`) showed no
measurable cost on a full station. Rerun them after touching `UpdatePhases`, `Merge` or the heat capacity.

## Out of scope

- **Supercritical fluids**, as above.
- **Henry's law, activity coefficients, and chemistry inside pipes.** Reagents in a pipe don't react with each other,
  and gases don't dissolve in liquids. Raoult's law (ideal mixing) is the only interaction.
- **Finite-volume pipes.** A pipe net is still one well-mixed mixture, so pressure drop along a run of radiators comes
  only from the radiators' own packet handling.
- **Direct heat transfer between two pipe nets.** The guidebook's answer is a small sealed chamber holding both sets of
  radiators.
