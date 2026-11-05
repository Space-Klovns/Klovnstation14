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
  Currently linked: Water, Oxygen, Nitrogen, NitrousOxide, Ammonia, CarbonDioxide, Hydrogen.
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
  heat. A device on the grid recovers `AtmosPumpHeatRecovery` (default 0.95) of the rest as electricity. What's left
  goes to the pump's room, or to an immutable heat sink with moles (planet air, lattice). With nothing to shed it into,
  in vacuum, it stays in the packet.
- **Throttling**: gas let downhill changes temperature by Joule–Thomson (`KsThrottling`, Shared). It uses the van der
  Waals constants only as a thermal correction. A gas without them is ideal and doesn't change. Tritium's inversion
  temperature is low enough that it warms.

This applies to all of atmos, not only fluid-carrying mixtures.

Every device goes through `Apply`, and `KsPumpHeatSystem` keeps the books, so no device has heat logic of its own.

- **The bill.** What the device doesn't recover is what it pays: gas heat plus room heat, divided by the heat scale,
  since `Apply` works in unscaled joules and a device's watts are scaled. Its load is its idle load plus that, up to
  `AtmosPumpMaxLoadMultiplier` times the idle load on top. Past that the work goes unpaid: throughput never changes.
- **Energy, not entropy.** The first law holds. The second doesn't: a powered refrigeration loop can beat Carnot,
  because its room no longer has to take the compressor's heat. The cycle tests run their pumps with `NeedsPower` off,
  which recovers nothing, so they still check the real balance.
- **Not blinking the APC.** `IsPoweredCalculate` asks for supply to match load, so one shortfall on an APC unpowers
  everything on it. An APC covers a jump of `supplyRampTolerance` and then ramps at `supplyRampRate`. Loads therefore
  climb every tick rather than in steps, and only while the APC's supply is within half its tolerance of its ramp and
  under 80% of its charge rate. A count-based slew limit isn't enough: a dozen pumps out-climb a 500 W/s ramp. Past its
  charge rate, or under a quarter charge, the APC gets its load back. A device that loses power holds idle for 10 s.
- **Saves.** `Load` is a `[DataField]` (`powerLoad`), so a raised load is saved. `KsPumpHeatComponent.IdleLoad` is saved
  with it, and the component stays until the load is back at idle, or each reload would ratchet the idle upward.
- **The readout.** `KsPumpHeatFootnote` reads the networked component, so a window only places it. The numbers are
  smoothed over 5 s, on the load's scale, so gas plus room is what the device is billed.

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

  Buffers are flushed when the device is destroyed or the component alone is removed. A device deleted outright
  takes its buffer with it, as anything deleted takes its contents.
- **`AbsorbFromTile`** is the reverse, for siphoning vents (draining the tile's puddle at `PuddleDrainRate`) and
  scrubbers (taking in smoke).
- **`QueueSpill` / `QueueCondensate`** defer puddle work out of atmos callbacks to the system's `Update`.

**Spills on destruction.** A pipe broken or destroyed by damage spills its share of the net's fluid once
(`OnNodeContainerDestroyed`, on `DestructionEventArgs`). A pipe that is merely *deleted* (an admin, a deconstruct)
spills nothing, the same as gas. Canisters, gas tanks and portable scrubbers follow the same rule: a broken one spills
what it held, and a deleted one takes it along.

**Cook-off in pipes** (`KsPipeCookOffSystem`). A pipe net whose fluid holds explosive reagents
(`ksExplosivePower`) and is hotter than the lowest `ksCookOffTemperature` among them rattles some of its pipes with
the jitter effect, then ruptures them. Only the net's fluid counts: liquid, solid and unlinked vapor. Linked gas,
such as plasma gas, is ordinary atmos, so a hot plasma line doesn't blow up.

- **Power per node** (total power ÷ node count) sets the time: `ReferenceSeconds` at `ReferencePowerPerNode`,
  inversely with density, clamped to `MinSeconds`–`MaxSeconds`. Below `MinPowerPerNode` the net is too dilute
  and nothing happens. At `InstantPowerPerNode` and above it ruptures on the first check, without rattling.
- **Total power** sets how many pipes rupture: one per `PowerPerRupture` (half a fuel tank), from one up to every
  node. Bends are picked first, as the weakest link, then the rest at random. The jitter grows as the cook-off
  progresses, and stops if the net cools, runs dry or splits off.
- **Warnings.** The rattling pipes groan (`MetalScrape`) more and more often as it goes on. Halfway, the first one
  "groans under the strain"; at 85% it "bulges, about to burst" (`cookoff.ftl`).
- **The chain.** At full progress, the first bend bursts, then the rest in order of distance from it, `ChainDelay`
  apart. Once started, the chain runs to its end even if the net cools. Each burst sprays `klovn.atmos.pipe_cookoff_spill_fraction` (default 30%) of
  its share of the explosive fluid onto its tile as liquid at the net's temperature, then explodes with an equal
  share of the rest. Hot enough, the spray lights. Starting the chain takes every explosive out of the net,
  so what is left of it doesn't go off again. Destruction spills the rest of each pipe's share of the net as usual.

A sealed net of hot liquid fuel boils, and the latent heat can pull it back below its cook-off point. It has to be
kept hot to go off.

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

Puddles also conduct heat to their neighbours (`PuddleSystem.Klovn.Conduction.cs`). Ice sheets are cached per tile like
puddles.

**Heat with the air.** Puddles, ice sheets, foam blankets and containers with `KsCookOff` all go through
`KsAirHeatExchangeSystem`, by conduction and radiation (`AtmosPuddleEmissivity`, or a blanket's own `Emissivity`):

| Around it | What happens |
| --- | --- |
| Air with moles | Exchanged exactly, and the air changes. A big enough change excites the tile. |
| Immutable air (a map's atmosphere) | A bath: the body heads for its temperature, and the air doesn't change. |
| Vacuum | Radiation alone, to `TCMB`. Check moles first, because space reports a heat capacity. |
| No mixture at all (a sealed tile) | Nothing. This isn't vacuum. |

Immutable air and vacuum take no gas, which has two consequences. A puddle radiating there falls below its boiling floor with
nowhere to boil to, so `FreezeTileEffect` lets it freeze; in air that can take gas, a reagent below its triple point still
never freezes. And ice under a map's air melts rather than sublimes.

A foam blanket is the exception: it has nothing to leave behind, so on immutable air its vapor just goes, and the air
pays nothing for it. Hot planet air boils foam at the full rate without cooling, and a hot puddle boils the foam over it
as it would indoors. A blanket stops a puddle's own exchange with the air, so skipping immutable air here would seal the
puddle's heat in.

**Simultaneous exchanges split their capacity.** Puddle–neighbour, puddle–air and ice–air/puddle steps all start from
the same temperatures and are applied together. Each one may only work with its share of a body's heat capacity, which
is the capacity divided by the number of contacts the body actually has (`CountHeatContacts`; what's over a puddle
counts unless a foam blanket covers the tile). If each step used the whole capacity, a small hot puddle between
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

## Reactions in pipes

`ksFluidReaction` prototypes (`Content.Shared/_KS14/Fluidmos/Reactions/`) run inside fluid mixtures, at a rate, stepped by
`KsFluidReactionSystem` from the top of `UpdatePhases`, so whatever they make settles into its phase the same tick.
Beaker `ReactionPrototype`s never run in pipes, and these never run in beakers. Keep it that way: a refinery
intermediate in an upstream recipe is a way round the refinery, and `KsRefineryReactionTests` rejects one.

- **Rate.** `ξ = n (1 - exp(-k dt))`, where `n` is the reaction units the limiting reactant allows and
  `k = rate * exp(activationTemperature * (1/referenceTemperature - 1/T))`. Exact first order, so it never overshoots.
- **Heat.** `enthalpy` is J per unit of reaction, game scale, positive endothermic. One step moves the temperature at
  most `MaxReactionDeltaT` (20 K), so a runaway takes several ticks. Energy is kept as the phase step keeps it, with
  each reagent's offset by phase (fusion heat as liquid, fusion plus latent as vapor), so a reaction that makes vapor
  from liquid pays to boil it, and `enthalpy` is only the chemistry.
- **Linked reagents** react from and into gas moles. Candidates are found from the fluid table, so every reaction
  needs at least one unlinked or condensed reactant. Only a reagent's entry with no extra data takes part.
- **Idle cost.** Candidates are cached on the mixture against the table's `KeysVersion`; a mixture colder than every
  candidate's `minTemp` returns after one comparison. Otherwise at most one step per `StepInterval` (0.5 s).
- **Gates.** `minTemp`/`maxTemp`, `minPartialPressure`, and `catalystReagent` (present, not consumed, such as steam).

A reaction with a `catalyst` runs only from a `KsCatalyticReactor`: an inline pipe vessel with one straight-through node
and a slot for a bed (`KsFluidCatalystComponent`). It steps the bed's reactions on its own pipe net with
`dt * share * activity`, where `share` is `BedVolume / net volume`, capped at 1. It has no chamber or flow setting: pumps
either side set the residence time. The bed also takes up the `poisons` of its `ksFluidCatalyst`, which leave the
mixture and cost it activity.

A step's extent is capped so the temperature moves at most `MaxReactionDeltaT`, counting both the reaction's heat and what
it does to the heat capacity. An extent too small to leave a product the reagent table would keep isn't run: it waits on
the mixture (`KsFluidMixture.DeferredExtent`) until it's big enough, so a trace product isn't lost to the table's minimum.

## Non-ideal mixing

Raoult's mole fraction is now an activity, `x γ`, in the phase step and at radiator walls (`ComputeActivities` in
`KsFluidmosPhaseSystem.Activity.cs`). A mixture with no non-ideal reagent takes the old path and does no lookups.

- **Margules.** `ln γ_i = Σ_j A_ij x_j - ½ Σ_j Σ_k A_jk x_j x_k`, with `A(T) = A * 298 / T`, clamped to 2, past which
  the two-suffix model predicts a false liquid split.
- **Where A comes from.** A `ksFluidPairInteraction` for two named reagents, else their `ksFluidMixingFamily` entries,
  else zero. A reagent joins a family with `mixingFamily` in its Fluidmos block. Prefer families: they avoid an N×N table.
- **Immiscible families** form separate liquid phases: `x` is taken within the phase, so each keeps its full vapor
  pressure, which is steam distillation. A reagent in no family dissolves in every phase.
- **Flashing.** A boiling liquid flashes down to its bubble point, where `Σ x_i γ_i Psat_i(T) = P`, by a few Newton steps
  (`KsPhaseMath.BubbleTemperature`). A single reagent still boils at its own `Tb(P)`.

Ethanol and water are fitted to their real azeotrope, about 89 mol% ethanol. Past it the vapor is poorer in ethanol than
the liquid.

### Freezing-point depression

`Tm' = Tm / (1 - ln(x γ) / k_fus)`, with `k_fus` from the optional `fusionEntropy` (default water's 2.65). `x` counts the
reagent's own solid as if it had melted and `x γ` is floored at 0.1, so ice dropped into a solvent doesn't melt all at
once. That also means a reagent past its depressed point freezes entirely, rather than down to a solubility limit. The
same equation is ideal solubility, which is how wax crystallises out of chilled gas oil; paraffin wax sets a real
`fusionEntropy` of 20 for this.

## Inseparable groups and the refinery

`KsReagentSeparationSystem.SplitGroup` rounds each member's share up or down at random, weighted so its expected value
is exact, and the shares always add up to what was asked for. Any fixed rounding would be a separator: a trace member
would stay behind through any number of small transfers.

Reagents with `ksInseparableGroup` can't be picked apart by machines. `KsReagentSeparationSystem` moves a selected
member's whole group in proportion: ChemMaster transfer and discard, every plumbing pull (a group passes an outlet if
any member may, and is pulled as one bundle, since plumbing otherwise pulls reagent by reagent, which separates a
mixture by itself), a plumbing reactor's targets and output, the smart fridge, and injector whitelists. Nothing is
refused, so tanks don't strand a mixture and filters don't deadlock. Passive physics is left alone.

The refinery's reagents are pseudo-components by boiling range (`Resources/Prototypes/_KS14/Reagents/Petroleum/`). Every
hydrocarbon stays unlinked, so gas filters can't see it, and has no cook-off value, since refinery nets run far past any
sensible cook-off point. Hydrogen is linked to its own atmos gas, so it can be filtered, pumped into canisters and burned
(`KsHydrogenFireReaction`, 2 H2 + O2 to steam). Hydrogen sulfide is unlinked vapor, and leaves a pipe as inert smoke.
Reactions are in `Resources/Prototypes/_KS14/Fluidmos/Reactions/`; the player's view of the chain is the guidebook's
Oil Refining page. That page and reagent cards draw reactions from their prototypes (`KsGuideFluidReactionEmbed`,
`KsGuideReagentPointsEmbed`), so a new reaction only has to be added to the page, and `KsFluidGuidebookTests` fails until it is.

### The demonstration refinery

`Resources/Maps/_KS14/Grids/refinery.yml` is a working plant (`loadgrid`): crude geyser, three stills, a platinum
reformer, debug power. `KsRefineryGridTests` loads it and runs it. It was generated by a throwaway script that built the
layout on an uninitialised map and saved it, and is the reference for the settings below, each of which took measuring:

- **A separator evacuates its inlet.** Its pump stops only when its *outlet* reaches `targetPressure`. Near vacuum,
  everything boils, even bitumen, so a still needs its vapor metered: a low `targetPressure` and a slow pump just
  after it, so that the outlet stays at target. Inlet pressure then rises to the bubble point.
- **A thermomachine heats what is in the net at that moment**, clamped so it can't overshoot. A net that drains at once
  can't take heat, whatever the heater's power. Keep `liquidRate` below what arrives, so liquid builds up.
- **Pump volumes are multiplied by `atmos.speedup`** (8 by default). The nominal rates in the map are an eighth of the
  real ones.
- **A pump doesn't limit feed**: a net just holds more until the same flow passes. The fluid port's `rate` does.
- **Crude chills itself as it flashes**, and wax will freeze in the feed line without a preheater.

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
| `KsFluidGuidebookTests` | Pipe reactions on reagent cards and the Oil Refining page |
| `Atmos/KsAirHeatExchangeTests`, `Atmos/KsPumpHeatTests` | Heat with the air; pump recovery, load and APC safety |

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

The original design brief, [fluidmos_plan.md](fluidmos_plan.md), has the full list of planned follow-ups in its
section 7, and the reasoning behind most of the decisions above. The main ones:

- **Supercritical fluids**, as above.
- **Henry's law.** Gases don't dissolve in liquids.
- **Finite-volume pipes.** A pipe net is still one well-mixed mixture, so pressure drop along a run of radiators comes
  only from the radiators' own packet handling.
- **Direct heat transfer between two pipe nets.** The guidebook's answer is a small sealed chamber holding both sets of
  radiators.
