# Fluidmos: reagents inside atmospherics

> **Status (2026-11-01):** this is the original design brief Fluidmos was built from. Phases 0–6 are implemented on
> the `fluidmos` branch. It is kept for its reasoning and for **section 7 (follow-ups)**, which lists what is still to
> do. Where it and the code disagree, the code and [fluidmos.md](fluidmos.md) are right: details changed during
> implementation (for example, CO2 is now linked to its gas, the phase separator runs on power, and heat exchanges
> split a body's capacity between its contacts). Line numbers cited below are from 2026-10-30 and have drifted.

## Context

Atmos pipes, machines and containers only carry gas. Fluidmos lets them carry chemistry reagents as well, as liquid
and as vapor. The reagents move with the gas through existing devices, and they boil or condense against real vapor
pressure curves. When they reach open air, they leave as **puddles** (liquid) and **smoke** (non-gas vapor). Tile
atmospheres never hold fluid data. Some reagents are opt-in linked to an atmos gas (Water ↔ WaterVapor). Their vapor
*is* that gas, so it spreads across tiles as gas and condenses back into liquid inside pipes.

Priorities: tiles must pay effectively nothing, fluid-less pipes must pay nothing, and every hot-path operation must
work without prototype lookups.

This file is the complete brief. It assumes `CLAUDE.md` and the repo's skills, and nothing else: no earlier
discussion is needed. Section 8 gives the build order. Read sections 0–5c for the design, and section 10 for the
traps.

Before editing any file this plan cites by line number, re-read it: the numbers were taken on 2026-10-30 and may have
drifted. The cited behaviour is what matters.

Settled decisions, already agreed with the maintainer. Don't revisit them; ask the maintainer if one blocks
implementation:
- **1u = 5 mL** of atmos volume, for displacement (`LitersPerUnit = 0.005`). Mass stays at 1 g/u (below).
- **Moles per unit use 1 g/u**, the codebase's existing convention (section 1).
- **Gas links are opt-in** and start with Water↔WaterVapor, Oxygen↔Oxygen, Nitrogen↔Nitrogen,
  NitrousOxide↔NitrousOxide and Ammonia↔Ammonia (maintainer decision: with N2O alone, whose Psat(293 K) of about
  5.2 MPa is past a pressure pump's 4500 kPa cap, a room-temperature fridge needed a volume pump near N2O's Tc; ammonia's
  is about 840 kPa). Plasma, Tritium and Frezon stay unlinked.
  - **Plasma must stay unlinked.** It is the station's fuel, used in nearly everything, and its working lore is
    *tritiated ethanol*. With a real boiling point (about 352 K, section 0a), a linked plasma gas would condense at
    room temperature, and every plasma canister and line would turn to liquid. Unlinked, the reagent behaves as a
    liquid fuel in pipes while the gas is untouched.
- **All reagent temperatures are Kelvin.** Section 0 standardises the YAML that isn't.
- **Pumps heat what they compress, adiabatically**, with an efficiency (section 5b). This applies to all of atmos.
- **Joule–Thomson applies to all throttling**, through van der Waals constants used only as a thermal correction
  (section 5c).
- **Frozen reagents on tiles live in a `KsIceSheet` entity**, modelled on `IceCrust` (section 4b).
- **Second phase** (not this plan): Henry's law, activity coefficients, chemistry in pipes. Finite-volume pipes are a
  separate project.

### Modelling principles (why the physics looks the way it does)

- **Game scale, not SI.** Every reagent has `SpecificHeat` 1 J/(K·u). The field allows per-reagent values, but none
  is set. So latent heats are per unit, scaled to keep real substances' latent-to-sensible ratio. Water's 540 J/u
  (2257 J/g over 4.18 J/(g·K)) is what `BoilTileEffect` already uses. Moles per unit treat 1u as 1 g, also
  `BoilTileEffect`'s convention, so pipes and puddles turn the same reagent into the same gas.
- **One vapor-pressure curve everywhere**: Clausius–Clapeyron anchored at the 1 atm boiling point. It is the
  Boltzmann escape factor `exp(-ΔH/RT)` in another form. Its steepness is `k = ΔS_vap / R`, a **real, per-mole**
  quantity: Trouton's rule (`k ≈ 10.6`) by default, with a per-reagent override for the known outliers (water 13.1).
- **The curve and the energy budget are separate numbers.** Never derive `k` from the game-scale latent heat per unit.
  That gives water `k ≈ 3.1` per mole (or 0.17 if the units are mixed up), which puts `Psat(293 K)` near 43 kPa
  instead of about 2.3, so rooms hold roughly 20× too much humidity. The game-scale latent and fusion heats (from
  `LatentHeatRatio` and Richards' rule, `FusionHeatRatio`) only set how much energy a phase change moves.
- **Rates from Hertz–Knudsen**: the net of evaporation and condensation in one signed term, with a tunable
  coefficient `α`, capped so it never overshoots equilibrium. One explicit step per update; never iterate or recurse
  inside a tick.
- **Phases per reagent.** Solid, liquid and vapor amounts are tracked separately, so ice in liquid oxygen is just two
  reagents in different phases.
- **Freezing is an energy-limited plateau**, not a statistical fraction. Below its melting point a pure substance
  freezes completely, as fast as its heat of fusion can leave. A Boltzmann "fraction frozen" would wrongly put ice in
  10 °C water.
- **Solids never move with a transfer**, so pumping from a slurry takes only the liquid.
- **No supercritical fluids**: linked gases stop condensing above their critical temperature.

Follow `CLAUDE.md`. New code goes in `Content.{Shared,Server,Client}/_KS14/Fluidmos/`, prototypes in
`Resources/Prototypes/_KS14/Fluidmos/`, and locale in `Resources/Locale/{en-US,ru-RU}/_KS14/Fluidmos/`. Mark every
edit to an upstream file with `/* KS14: ... */` or `// KS14 start/end`.

## Grounding: existing code this builds on

- `Content.Shared/Atmos/GasMixture.cs`: a `sealed partial` class with `float[] Moles`, `Volume`, `Temperature` and
  `Immutable`. Its `RemoveRatio`, `Remove(amount)` (→ `RemoveRatio(amount / TotalMoles)`), `RemoveVolume`,
  `CopyFrom`, `Clear`, `Multiply`, `Clone` and `Equals` are the only ways its contents move. `TotalMoles` is
  `NumericsHelpers.HorizontalAdd(Moles)`, and `Pressure` is `TotalMoles * R * T / Volume`.
- `SharedAtmosphereSystem.Gases.cs`:
  - `Merge(receiver, giver)` (l.202) mixes temperature by heat capacity, then does `NumericsHelpers.Add`.
  - `GetHeatCapacity(mixture, applyScaling)` (l.161) and the overload at l.241 both go through
    `GetHeatCapacityCalculation(moles, space)`, which has the `HeatScale` divisor pre-applied.
- `AtmosphereSystem.Gases.cs` (server):
  - `DivideInto` (l.106) splits a pipe net. It doesn't modify the source.
  - `ReleaseGasTo` (l.152) and `PumpGasTo` (l.185) guard on `TotalMoles > 0`.
  - `React(mixture, holder)` (l.289) is called by `PipeNet.Update`, `GasCanisterSystem`, `GasTankSystem`,
    `PortableScrubberSystem` and `CryoPodSystem`.
- `PipeNet.cs`:
  - `Air` is created in the field initialiser.
  - `RemoveNode` calls `Air.Multiply(1 - pipe.Volume/Air.Volume)` for deleted pipes.
  - `AfterRemake` calls `DivideInto`.
- `TileAtmosphere.Air` is a plain field, reassigned in a few places. Tiles must never be fluid-capable, so capability
  is **opt-in**, not opt-out.
- Devices that merge into a **tile** mixture:
  - `GasVentPumpSystem` (l.161), `GasVentScrubberSystem` (siphon l.125; the scrub remix at l.121 is tile→tile)
  - `GasPassiveVentSystem`, `GasOutletInjectorSystem`, `GasCanisterSystem`, `GasTankSystem`
  - `AtmosUnsafeUnanchorSystem`, `PortableScrubberSystem`
  - KS: `GasPistonSystem`, `GasGrenadeCompressorSystem`, `KsTileEffectAtmosSystem` (check each one)
- `ReagentPrototype`:
  - `SpecificHeat` is J/(K·u).
  - `BoilingPoint` and `MeltingPoint` are `float?`. Upstream YAML stores them almost entirely in Celsius, with a few
    values already in Kelvin. Section 0 standardises them to Kelvin, per the game-wide rule. Fluidmos then reads
    `BoilingPoint` directly.
  - There are 421 reagents.
  - Partials already exist (`ReagentPrototype.Klovn.*.cs`).
- `GasPrototype.Reagent` links gases to reagents for lungs. Fluidmos doesn't reuse it; it uses its own opt-in link.
- `ReagentId` equality uses LINQ `Except` and must stay out of hot loops. `ReagentData` (e.g. `DnaData`) is
  `[NetSerializable]` with `Clone`/`Equals`/`GetHashCode`.
- `SharedPuddleSystem.TrySpillAt(EntityCoordinates/TileRef, Solution, out puddle, sound)` and
  `PuddleSystem.TryGetPuddle(TileRef, out uid)` are the puddle API.
- `SmokeSystem.StartSmoke(uid, solution, duration, spreadAmount)` starts smoke on an entity that has already been
  spawned (see `DragonSystem.cs:107`).
- `UpdatePuddleEvaporationReaction` only scales puddle evaporation. It doesn't convert matter, so there's no
  conservation conflict.
- **`Content.Shared/_KS14/TileEffects/Effects/BoilTileEffect.cs`** already boils puddle reagents into tile gas. It is
  the reference implementation for game-scale phase-change energy, and Fluidmos must agree with it.
  - It runs on puddles through the KS tile-effect system, with `context.RemovedVolume` for deferred removal. Water,
    FirefightingFoam and CO2 use it, via `_KS14/Reagents/Partials/water.yml`, `firefighting.yml` and
    `carbon_dioxide.yml`.
  - `Rate` caps the units per second.
  - `LatentHeat` is in J per unit, default 540 (water, against a specific heat of 1 J/(K·u)).
  - `MolesPerUnit` defaults to `1 / GasMolarMasses[gas]`, i.e. 1 g/u.
  - `Sublimate` boils below the boiling point, taking the latent heat from the air.
  - The boiled reagent's sensible heat goes into the air with its gas, and the latent heat leaves with the vapor.
  - CO2 overrides it with `molesPerUnit: 4` and `latentHeat: 10200`.
  - It currently reads `BoilingPoint` as Celsius (l.81–88).
- `ExtinguishTileReaction.EnergyPerUnit` (250000) and `KsVaporizeTileReaction` (CO2, `energyPerUnit: 30000`) are
  separate firefighting tunings. Leave them alone.

## 0. Standardise reagent boiling and melting points to Kelvin (separate commit, first)

All game temperatures are Kelvin; Celsius appears only in diegetic player-facing UI. `ReagentPrototype.BoilingPoint`
breaks this rule, and its readers patch over it with `+ Atmospherics.T0C`.

**YAML.** There are about 64 reagent `boilingPoint:` values. Convert each with a value-swap marker that keeps the
original, as `CLAUDE.md` §3 requires:

```yaml
boilingPoint: 373.15 # KS14: 100.0 -> 373.15, Kelvin
```

- **Celsius (+273.15)**: every other upstream value under `Resources/Prototypes/Reagents/`. Examples: Water 100 →
  373.15, Oxygen -183 → 90.15, WeldingFuel -84.7 → 188.45, Mercury 356.73 → 629.88.
- **Already Kelvin, keep**: Benzene 353.2.
- **Sentinel, keep**: UnstableMutagen 340282300 means "never boils".
- **Fork-owned files** have no marker; edit them directly:
  - `_KS14/Reagents/firefighting.yml` (FirefightingFoam 100 → 373.15)
  - `_KS14/Reagents/Partials/carbon_dioxide.yml` (-78.5 → 194.65)
  - `_KS14/Reagents/chemicals.yml` Klovnide 415.15. This looks like Kelvin already; confirm against its git history
    before changing it.
- **Not a reagent, leave alone**: `foam_blanket.yml` `boilingPoint: 463` is `KsFoamBlanketComponent`'s own field,
  already in Kelvin.
- **`meltingPoint`**: same treatment, in the same pass. No code reads it today, but it has the same ambiguity.

Also grep the `_KsModule*` submodules for `boilingPoint:` and `meltingPoint:`.

**Code.** Put a KS-marked doc comment on `ReagentPrototype.BoilingPoint` and `MeltingPoint`: "Kelvin, at 101.325 kPa."
Then remove the Celsius conversion from every reader:
- `Content.Server/Chemistry/TileReactions/ExtinguishTileReaction.cs:82` drops `+ Atmospherics.T0C`, with a
  `/* KS14: ... */` marker.
- `Content.Shared/_KS14/TileEffects/Effects/BoilTileEffect.cs:81-88` (fork-owned, no marker): rename
  `boilingPointCelsius` and drop `+ Atmospherics.T0C`.
- `KsFoamBlanketComponent.BoilingPoint` (`T0C + 100f`) is its own field and is already Kelvin. Leave it.
- The fork tests drop their `+ T0C` and their "in Celsius" comments:
  - `KsCarbonDioxideTest.SublimationPoint`
  - `ExtinguishEnergyConservationTest` (l.107)
  - `KsFirefightingTest` (l.284)
- `KsFirefightingTest:538` and `KsFoamBlanketHeatTest` read the component field, so no change.
- Grep `BoilingPoint` and `MeltingPoint` across `Content.*`, guidebook XML and `.ftl` for any player-facing display.
  None was found, but if one exists, convert to °C at display time only.

**Test.** Add `KsReagentTemperatureUnitsTest`: every reagent's boiling and melting points are within (0, 10000) K or
are the sentinel, and `MeltingPoint < BoilingPoint` where both are set. A Celsius value reintroduced by an upstream
merge then fails CI. Run the affected firefighting, CO2 and extinguish tests before and after the change.

### 0a. Addendum: plasma and tritium reagent temperatures (own small commit; apply now if phase 0 is already done)

Both are in `Resources/Prototypes/Reagents/gases.yml`. Keep the **original upstream value** in each marker, as
`CLAUDE.md` §3 requires.
- **Plasma** (lore: tritiated ethanol). Upstream's values are self-described as "random values picked between CO2 and
  O2". Replace them with ethanol's, so ground plasma is a flammable liquid in pipes that spills as puddles and boils
  to smoke when hot:
  ```yaml
  boilingPoint: 352 # KS14: -127.3 -> 352, Kelvin; tritiated ethanol
  meltingPoint: 159 # KS14: -186.4 -> 159, Kelvin; tritiated ethanol
  ```
- **Tritium** has no boiling point, so Fluidmos would treat liquid tritium as non-volatile and it would never
  vaporise. Add T2's real values:
  ```yaml
  boilingPoint: 25 # KS14: added, Kelvin
  meltingPoint: 20.6 # KS14: added, Kelvin
  ```
- Both stay unlinked (see Context), so neither change touches plasma or tritium **gas**.
- `KsReagentTemperatureUnitsTest` must still pass.

## 1. Core data (Shared)

### `Content.Shared/_KS14/Fluidmos/KsFluidmos.cs`: constants

Use `const`, not cvars, so the hot path does no lookup.

| Constant | Value | Meaning |
| --- | --- | --- |
| `LitersPerUnit` | 0.005 | Volume of 1u in atmos (1u = 5 mL) |
| `MinLiquidUnits` | 0.001 | Smaller liquid amounts are dropped, like `GasMinMoles` |
| `MinVaporMoles` | `Atmospherics.GasMinMoles` | Smaller vapor amounts are dropped |
| `MinFreeVolumeFraction` | 0.02 | Floor on the volume left for gas |
| `HydraulicStiffness` | e.g. 5000 kPa per 1% overfill | Pressure added once liquid exceeds the free-volume floor. **Linear, not exponential** (see section 2) |
| `MaxHydraulicPressure` | `10 * Atmospherics.MaxOutputPressure` | Hard cap on the hydraulic term, so overfill can never produce an infinite or NaN pressure |
| `LiquidPseudoMolesPerUnit` | 0.1 | How strongly liquid counts toward transfers; tunes liquid flow speed |
| `GramsPerUnit` | 1 | Mass of 1u, for mole counts; matches `BoilTileEffect` (see the mass scale below) |
| `DefaultMolarMass` | 100 g/mol | For reagents with no molar mass of their own |
| `HertzKnudsenAlpha` | tuned (see section 3) | Evaporation/condensation coefficient in pipes |
| `PipeInterfaceAreaPerLiter` | 0.0025 m²/L | Liquid–gas interface area in a pipe net (0.5 m² per 200 L node) |
| `MaxPhaseDeltaT` | 5 K | Most a single step's latent heat may change the temperature |
| `DefaultVaporizationEntropy` | 10.6 | Default `k = ΔS_vap / R` (dimensionless; Trouton's rule, 88 J/(mol·K)). Sets the steepness of the `Psat` curve and nothing else |
| `LatentHeatRatio` | 540 / 373.15 ≈ 1.447 | Default latent heat per unit = this × `BoilingPoint` × `SpecificHeat` (game-scale energy only, calibrated to give water exactly `BoilTileEffect`'s 540 J/u). Never used for the curve |
| `FusionHeatRatio` | 80 / 273.15 ≈ 0.293 | Default heat of fusion per unit = this × `MeltingPoint` × `SpecificHeat` (Richards' rule, calibrated to water's real 334 J/g over 4.18 J/(g·K)) |
| `SpillThresholdUnits` | 1 | Liquid buffered before a puddle spill |
| `SmokeThresholdUnits` | 0.5 | Vapor buffered before a smoke release |

### `ReagentPrototype.Klovn.Fluidmos.cs`: a partial with one optional data block

The boiling point comes from the standardised `ReagentPrototype.BoilingPoint` (section 0), so there is no second copy.
`[DataField] KsFluidmosReagentData? Fluidmos` is optional and holds:
- `MolarMass` (g/mol). A linked reagent takes its gas's `GasPrototype.MolarMass` and must not set its own; otherwise
  the default is `DefaultMolarMass`.
- `LatentHeat` (**J per unit**, on the game's 1 J/(K·u) scale like `BoilTileEffect`; default
  `LatentHeatRatio * BoilingPoint * SpecificHeat`)
- `FusionHeat` (J per unit; default `FusionHeatRatio * MeltingPoint * SpecificHeat`)
- `VaporizationEntropy` (dimensionless `ΔS_vap / R`, the curve's `k`; default `DefaultVaporizationEntropy`). Set it
  from the real heat of vaporization, `ΔH_vap / (R * Tb)`, for reagents that break Trouton's rule.
- `MolesPerUnit` (optional override, as CO2's `BoilTileEffect` has 4)
- `Gas` (`Gas?`, the opt-in link)

The heats are per unit, not per mole. With every reagent at 1 J/(K·u), a per-mole Trouton value would put boiling 20×
out of proportion to warming. The defaults keep real substances' latent-to-sensible ratio, which is exactly how
`BoilTileEffect`'s 540 was derived.

`VaporizationEntropy` is the opposite: a real per-mole value, because the curve has to match real vapor pressures
for humidity, boiling and condensation to land where players expect. The two are deliberately decoupled. On the game
scale, the Clausius–Clapeyron latent heat and the energy a phase change moves disagree, and that is fine, because
energy conservation only uses `LatentHeat`.

### Mass scale: moles per unit

`molesPerUnit = MolesPerUnit override ?? GramsPerUnit / MolarMass`, at **1 g/u**. That is the convention
`BoilTileEffect` already uses: "one mole per molar mass in units, treating a unit as a gram". A puddle boiled by the
tile effect and a pipe boiled by Fluidmos therefore make the same gas from the same reagent.

Volume is **5 mL/u** for displacement. Mass and volume are deliberately decoupled: the implied liquid density is
0.2 g/mL, against water's real 1. Mass stays at 1 g/u for agreement with `BoilTileEffect`.

**Why**:
- At 1 g/u, water is 0.0556 mol/u. A closed pipe of hot water doesn't boil away completely: it boils until the vapor
  reaches `Psat`, about 245 kPa at 400 K. That makes a boiler, not a bomb.
- At 5 g/u (real density at 5 mL/u), the same amount of water would hold five times the moles. Puddles, tile
  humidity and `BoilTileEffect` would then disagree.

**Displacement at 5 mL/u is small.**
- A 200 L node holds 40,000u before it is full.
- A beaker (50u) takes 0.25 L, and a full fluid port (300u) 1.5 L.
- So in normal play, liquid barely changes `FreeVolume`. The hydraulic term and the ice clog (section 1, Keys and
  amounts) become safety nets that need a very large amount of fluid, not something players meet routinely. That is
  intended.
- Tests that need an overfull net derive the amount from `Volume / LitersPerUnit`; they don't use a fixed number of
  units.
- A saturated 293 K tile holds about 52u of water as humidity (`Psat` ≈ 2.8 kPa with `k` = 13.1), so puddles and tile
  humidity work at the same scale (section 4a).

**Put the conversions in one shared helper**: `Content.Shared/_KS14/Fluidmos/KsPhaseMath.cs`, covering `Psat`,
`Tb(P)`, moles per unit, and the latent and fusion heats per unit. `BoilTileEffect` and Fluidmos both call it, so the
two can't drift.

Molar mass also feeds the Hertz–Knudsen rate.

### Phases and volatility

Each reagent can be solid, liquid or vapor.
- **Volatile**: `BoilingPoint` is non-null and not the sentinel. That covers about 60 reagents with no extra YAML.
- **Non-volatile**: no boiling point. Never vaporises.
- **Freezable**: `MeltingPoint` is set (section 0 standardises it).
- Phases are tracked per reagent. Ice inside liquid oxygen, or solid potassium in water, are just different amounts
  in different fields.

### Seed data

The `fluidmos:` blocks go in partial prototypes: `- type: !PartialOnly reagent` in
`Resources/Prototypes/_KS14/Fluidmos/Partials/reagents.yml`. Register the directory in `KsEntryPoint.PartialPrototypePaths`
(`Content.Shared/_KS14/Entry/KsEntryPoint.cs`).

| Reagent | Boiling point (K, after section 0) | Molar mass (g/mol) | Moles per unit | `vaporizationEntropy` (real `ΔH_vap / (R * Tb)`) | Gas link |
| --- | --- | --- | --- | --- | --- |
| Water | 373.15 | 18 (from gas) | 0.0556 | 13.1 | WaterVapor |
| Oxygen | 90.15 | 32 (from gas) | 0.0313 | 9.1 | Oxygen |
| Nitrogen | 77.35 | 28 (from gas) | 0.0357 | 8.7 | Nitrogen |
| NitrousOxide | 185.15 | 44 (from gas) | 0.0227 | 10.7 | NitrousOxide |
| Ethanol | 351.35 | 46 | 0.0217 | 13.2 | none |
| Ammonia | 240.15 | 17 | 0.0588 | 11.7 | **Ammonia gas** (maintainer decision). Upstream's gas `molarMass: 44` is a copy-paste; the gas partial sets 17, since a linked reagent takes its gas's molar mass. Its puddles evaporate into the gas rather than boiling to smoke. Condenses below its Tc, about 408 K (`KsCondensationAmmonia`). |

Any other reagent uses the defaults (0.01 mol/u, `k` = 10.6). WeldingFuel (188.45 K, the acetylene value) is therefore mostly
vapor at room temperature, and leaves pipes as smoke.

### `KsFluidmosPropertiesSystem` (Shared `EntitySystem`): the property table

- Builds a `Dictionary<ProtoId<ReagentPrototype>, KsFluidReagentProperties>` of immutable classes. Each holds the
  specific heat, moles per unit, boiling point, the curve's `k` (`VaporizationEntropy`, never derived from
  `LatentHeat`), the latent heat, the linked gas index (or -1) and the colour.
- Each entry also holds `MinActiveTemperature`: the temperature where `Psat` reaches 0.01 kPa, solved once from the
  same curve. Below it the phase step skips the reagent, so metals and salts cost nothing.
- Also builds `KsFluidReagentProperties?[] ByGas`, indexed by gas, for the linked reagents.
- Rebuilds on `Initialize` and on `PrototypesReloadedEventArgs` when reagents were modified.
- Validates at build time and logs an error for:
  - a gas linked from two reagents
  - a non-positive boiling point
  - a linked gas whose reagent is missing
- Instance state only. No static mutable state, because pooled test servers share a process.

### `KsReagentDataSet`: immutable data, normalised once

- `ReagentData[] Items` plus a precomputed, order-insensitive hash (the sum of the element hashes).
- `Equals`: `ReferenceEquals` → hash → count → per-element containment.
- Built only at the Solution↔fluid boundary. An empty or null `ReagentId.Data` becomes `null`.
- Split and merge share the same instance, so the hot path compares by reference.

### Keys and amounts

- `KsFluidReagentKey`: `record struct (ProtoId<ReagentPrototype> Prototype, KsReagentDataSet? Data)`. It holds no
  runtime indices, so it is safe across pooled servers and in map saves.
- `KsFluidAmount`: `struct { float Solid /*u*/; float Liquid /*u*/; float Vapor /*mol, unlinked reagents only*/ }`.
  A linked reagent's vapor lives in `GasMixture.Moles[gas]`, and its `Vapor` stays 0.
- **Solids never move with a transfer.** `RemoveRatio` leaves `Solid` behind, so a pump
  drawing from a slurry takes the liquid and leaves the ice.
  - Solids still split by volume when a net is divided (`DivideInto`), because they physically sit in those pipes.
  - Solids displace volume like liquid does, so a net packed with ice raises the hydraulic pressure and pumps stop
    feeding it: a frozen pipe clogs. At 5 mL/u this takes about 40,000u of ice per 200 L node, so it's rare.
  - Pipe deconstruction spills its share of solids as well.
  - Melting the solid is the only way to get it out.

### `KsFluidMixture`: `[Serializable, DataDefinition]`

- `[DataField(customTypeSerializer: KsFluidReagentsSerializer)] Dictionary<KsFluidReagentKey, KsFluidAmount> Reagents`.
  The serializer writes a sequence of `{reagent, solid, liquid, vapor, data}` and lives in
  `Content.Shared/_KS14/Fluidmos/Serialization/`, mirroring `GasArraySerializer`.
- Cached `[ViewVariables]` values, maintained incrementally:
  - `LiquidUnits`, `VaporMoles` and `HeatCapacity` (unscaled J/K)
  - `_opsSinceRecount`: recount after 16 operations, following `Solution.CheckRecalculateHeatCapacity`
  - `HeatCapacityDirty`
- `LiquidUnits` and `VaporMoles` can be recounted from the dictionary alone. `HeatCapacity` needs the property table,
  so it is refreshed in the phase step or in `GetHeatCapacity`, both of which have access to the system.
- Quiescence state: `LastTemperature`, `LastPressure`, `CompositionChanged`.
- Linear operations, none of which need prototypes:
  - `RemoveRatio(ratio)` returns a new mixture: a new dictionary sized to `Count`. Amounts below the minimums are
    dropped on both sides, and the caches scale.
  - `Add(other, fraction = 1)`: caches add.
  - `Multiply`, `Clear`, `CopyFrom`, `Clone`, `IsEmpty`, `Equals`.
- Boundary operations, which take the property table:
  - `AddSolution(Solution, KsFluidmosPropertiesSystem)`: liquid in, `ReagentId` → key.
  - `TakeLiquidAsSolution(units)`: rebuilds the `ReagentId`s with cloned data lists, converting with
    `FixedPoint2.New(float)`. Truncation residue stays in the mixture.
  - `TakeVaporAsSolution(moles)`: moles → units using `MolesPerUnit`.
- No LINQ, no `CollectionsMarshal`, no `stackalloc` (the sandbox rejects them).

## 2. `GasMixture` integration

### Which mixtures hold fluid

There is no separate type. Two members on `GasMixture` decide everything:

| | `FluidCapable` false (tiles, space, lungs, everything else) | `FluidCapable` true (pipe nets, canisters, tanks, new devices, and packets taken from them) |
| --- | --- | --- |
| `Fluid == null` | The usual case: one null check per hot path. | A dry pipe: same cost. |
| `Fluid != null` | Must never happen. `Merge` asserts, logs and drops. | Carries reagents: fluid logic runs. |

- `Fluid` is created lazily the first time reagents enter, and nulled by the phase step once it's empty.
- `FluidCapable` is set by the owner and is not saved. `RemoveRatio` and `Clone` results inherit it.
- Capability is opt-in because `TileAtmosphere.Air` is reassigned in several places, so an opt-out could be missed.
- Every route from a device into a tile goes through `ReleaseToTile` (section 4c), which detaches the fluid first. A
  route that skips it fails the Debug device tests through the `Merge` assert.

### Networking

`GasMixture` is `[Serializable]` and travels in some network messages and UI states. So mark `KsFluidMixture`,
`KsFluidReagentKey`, `KsFluidAmount` and `KsReagentDataSet` `[Serializable, NetSerializable]`. `ReagentData` already
is.

UI states should send a flattened `KsFluidEntry[]` (prototype id, solid u, liquid u, vapor u), not the mixture: it's
smaller, and the client has no use for the keys.

The phase step is server-only. Shared code only carries the data and does the linear operations.

### Changes

Add `GasMixture.Klovn.Fluid.cs` and KS-marked one-liners in `GasMixture.cs`.

- `[DataField] public KsFluidMixture? Fluid;` stays null unless fluid is present. Immutable mixtures always keep it null.
- `public bool FluidCapable;` is **not** a DataField. It is set by the owner, and mixtures produced by
  `RemoveRatio` or `Clone` inherit it.
- `FreeVolume` = `Volume - Fluid.LiquidUnits * LitersPerUnit`, clamped to at least `Volume * MinFreeVolumeFraction`.
- `TransferMoles` = `TotalMoles + Fluid.VaporMoles + Fluid.LiquidUnits * LiquidPseudoMolesPerUnit`.
- `Pressure`: when `Fluid` is null it is unchanged. Otherwise it is
  `(TotalMoles + VaporMoles) * R * T / FreeVolume + hydraulic term`. The hydraulic term is
  `min(HydraulicStiffness * overfill%, MaxHydraulicPressure)` once liquid exceeds the free-volume floor, which makes
  pumps stop and the existing overpressure mechanics fire.
  - **Keep it linear.** An exponential term was considered and rejected.
    - Pumps already stop before a net is 1% overfull, so a steeper curve changes nothing the game reads.
    - The gas term `nRT / FreeVolume` already stiffens hyperbolically as liquid fills the net.
    - An exponential makes the explicit liquid-flow step oscillate between two overfull nets.
  - **The clamp is required.** Overfill is unbounded: a deleted pipe shrinks the net, `Merge` never checks capacity,
    and `DivideInto` can concentrate liquid. Transfer helpers divide by pressure, so an infinite pressure gives NaN,
    and `SetMoles` throws on NaN inside the atmos loop.
  - Nothing in this repo ruptures or explodes from pipe or tank overpressure. Pressure is read only by device targets,
    `MaxOutputPressure`, the scrubber and filter cut-offs, and the unanchor leak check.
- `TotalMoles` **stays gas-only**, so gas fractions and analyzer percentages keep their meaning.
- `Remove(amount)` uses `TransferMoles` as its denominator.
- `RemoveRatio`, `CopyFrom`, `Clear`, `Multiply`, `Clone`, `Equals`, `GetHashCode`: each gets one null-guarded fluid
  line.
- New methods:
  - `RemoveRatio(ratio, includeLiquid: false)` for gas-tank internals and the gas side of the phase separator.
  - `RemoveLiquidRatio(ratio)` for the liquid side of the phase separator.

`SharedAtmosphereSystem.Gases.cs`, KS-marked:
- `GetHeatCapacity`, both overloads: add `Fluid.HeatCapacity / HeatScale` so scaling stays consistent with gas.
- `Merge`:
  - If the giver has fluid and the receiver is `FluidCapable`, ensure the receiver's `Fluid` and add to it.
  - Otherwise `DebugTools.Assert(false, ...)`, log an error, and drop the fluid. This safety net makes a missed
    tile-facing path fail the Debug tests.
- The temperature mixing already uses `GetHeatCapacity`, so it picks up fluid automatically.

`AtmosphereSystem.Gases.cs` (server), KS-marked:
- `DivideInto`: give each receiver `source.Fluid` × fraction; the source is not modified, as for gas.
- `ReleaseGasTo` and `PumpGasTo`: the `TotalMoles > 0` guard becomes `TransferMoles > 0`.
- `React`: the first line is `if (mixture.Fluid != null) _fluidmos.UpdatePhases(mixture, holder);`.
- Then grep `Content.Server/Atmos/Piping` and `_KS14/Atmos` for `TotalMoles` guards on pipe-side mixtures, and change
  them the same way. Otherwise pure-liquid pipes stall.

Mark fluid capability, KS-marked:
- `PipeNet`'s constructor or field initialiser, and its `Air` setter.
- `ComponentInit` of the canister and gas tank components. It is not a DataField, so `UninitializedSaveTest` stays
  green.
- The new devices below.

`PipeNet.RemoveNode` (deleted pipe): `Multiply` already removes the fluid share. Also spill that share as a puddle at
the pipe, via the tile system below.

## 3. Phase equilibrium (Server `KsFluidmosPhaseSystem`)

`UpdatePhases(GasMixture mixture, IGasMixtureHolder? holder)` runs for every fluid mixture on every `React`:

1. **Skip when quiescent**: if `!CompositionChanged`, `|T - LastT| < 0.05 K` and the relative pressure change is
   below 0.1%, return. Pressure captures gas merged in from outside.
2. Refresh the heat capacity if it is dirty. If the fluid is empty, set `mixture.Fluid = null`.
3. For each volatile reagent above its `MinActiveTemperature`: the dictionary entries, plus each linked gas with
   `Moles[gas] > 0` and no entry, so pumped-in WaterVapor can condense. Linked vapor below that temperature condenses
   fully.
   - `Psat = 101.325 * exp(clamp(k * (1 - Tb/T), -50, 20))` (Clausius–Clapeyron).
   - `n_v` is `Moles[gas]` for a linked reagent, otherwise `Amount.Vapor`.
   - **Raoult's law**: `n_eq = x_i * Psat * FreeVolume / (R * T)`, where `x_i` is the reagent's mole fraction of the
     total liquid moles, computed once per step. Without it, a trace of ethanol in water evaporates as if it were
     pure, which makes separation instant and perfect. With it, distillation depends on relative volatility. When the
     liquid is gone, `x_i` is 1 for the condensation check, so vapor can always condense.
   - **Rate (Hertz–Knudsen)**: the net flux is evaporation minus condensation, so a single signed term:
     `J = α * (x_i * Psat - p_i) / sqrt(2π * M * R * T)` mol/(m²·s), with `p_i = n_v * R * T / FreeVolume`, `M` in
     kg/mol and pressures in Pa. Multiply by the interface area (`Volume * PipeInterfaceAreaPerLiter`) and by the
     step's `dt` to get `Δn`.
     - This replaces the arbitrary relaxation constant. Lighter molecules exchange faster, and a bigger
       liquid/gas interface exchanges faster.
     - `α` is a tuning knob, not a physical constant. Set it so a 200 L node of water at 300 K comes within 90%
       of saturation in about 5 s; the test pins that.
   - **Pressure regime**: once `T` exceeds `Tb(P_total)`, the reagent flashes, instantly and limited only by energy,
     down to `Tb(P_total)`. This is the same formula as `BoilTileEffect` (section 5c, "Flash"). Below that,
     Hertz–Knudsen surface evaporation applies. Raising the pressure raises `Tb(P)`, so a pressurised boiler holds
     hot water that only evaporates at the surface, while venting it makes it flash. Free volume shrinking as liquid
     fills a pipe feeds back through `p_i`.
   - **Never overshoot**: cap `Δn` at the gap to equilibrium (`n_eq - n_v`), at the available liquid
     (`Liquid * MolesPerUnit`) or vapor, and at `|Δn * LatentHeat| ≤ C * MaxPhaseDeltaT`. Hertz–Knudsen at real
     coefficients is stiff, and an explicit step without these caps oscillates. There's one explicit step per
     `React`, with no iteration or recursion, so the cost per tick is bounded.
   - Move the amounts, then **recompute the temperature from energy conservation**:
     `T' = (C_before * T - Δunits * LatentHeat) / C_after`.
     - The phase change also changes the heat capacity. A unit of liquid is 1 J/K, while its vapor is
       `molesPerUnit` × the gas's molar heat. So `T -= Q / C` would quietly create or destroy energy.
     - Follow `BoilTileEffect`'s accounting, which already handles exactly this mismatch (see its `MolesPerUnit`
       remark), and its heat-scale handling for the gas side. Check `GasReactionPrototype` for the convention as well.
4. **Freezing and melting**, for reagents with a `MeltingPoint`. These are energy-limited, exactly like puddle
   boiling.
   - Below `Tm`, the sensible heat deficit freezes liquid: `units = (Tm - T) * C / FusionHeat` (`FusionHeat` is per
     unit), capped by the liquid available. Freezing releases `FusionHeat` and warms the mixture back towards `Tm`.
   - Above `Tm`, solid melts the same way, in reverse.
   - So a mixture sits at `Tm` as a slurry until the heat budget has been paid. This is the real behaviour; a
     Boltzmann fraction is not (see "Modelling principles" under Context).
   - Freezing-point depression is out of scope.
   - Solids are excluded from Raoult's `x_i`.
   - **Sublimation**: a solid with no liquid of the same reagent evaporates from the same `Psat` curve, at `x = 1`. A
     CO2 or ice block in vacuum therefore sublimes.
5. Store `LastT` and `LastP`, and clear `CompositionChanged`.

`dt` is the time since this mixture's last `React`, using the atmos process interval the pipe net, canister or tank
already runs at.

Cost: a few reagents per mixture, each with one `MathF.Exp` and one `MathF.Sqrt`, and only on mixtures that are both
fluid-bearing and non-quiescent. Cache `1/sqrt(2π * M * R)` per reagent in the property table, so a step needs only
`sqrt(T)`. Tiles pay a single null check in `React`.

## 4a. Puddles are the tile's fluid (phase exchange with tile air)

A tile mixture never holds fluid. Its fluid is the tile's single puddle, from `PuddleSystem.Klovn.PuddleTileCache.cs`
(`_gridPuddleCache`, `TryGetCachedPuddle`).

Puddle → tile phase change **already exists** as `BoilTileEffect`, which runs on puddles through the KS tile-effect
system. Fluidmos extends that effect rather than adding a parallel boiler. `BoilTileEffect` is fork-owned, so it is
edited directly, with no markers. Heat exchange with the air stays in `PuddleSystem.Klovn.AirHeatExchange.cs`, which
already warms a puddle up to its boiling point.

### Extend `BoilTileEffect`

- **Shared math.** Read the boiling point, moles per unit and latent heat through `KsPhaseMath` (section 1).
  - `LatentHeat` and `MolesPerUnit` become nullable overrides. Null takes the reagent's Fluidmos value, so water
    still gets 540 J/u and `1 / 18` mol/u.
  - CO2 keeps its explicit `latentHeat: 10200` and `molesPerUnit: 4`.
- **Pressure-dependent boiling point.** Replace the fixed `Tb` with `KsPhaseMath.BoilingPointAt(P_tile)`:
  `Tb(P) = 1 / (1/Tb - ln(P / 101.325) / (k * Tb))`, with `P` clamped to at least 0.1 kPa.
  - Water in a spaced room boils away at low pressure, and cools as it does.
  - A pressurised room raises the boiling point.
  - At 101 kPa nothing changes, so the existing firefighting tests should hold. Run them to check.
- **`Smoke` mode**, for volatile reagents with no gas. Instead of `Moles[gas]`, the boiled units go to the tile's
  smoke: add to existing smoke if there is any, otherwise spawn it, buffered as in 4c. A hot puddle of fuel boils into
  a toxic or flammable cloud.
- **`Evaporate` mode**, for linked reagents below `Tb(P)`. This is the Hertz–Knudsen humidity exchange below. It
  takes its latent heat from the solution, unlike `Sublimate`, which takes it from the air.
- **Who gets the effect.** Add it via partial prototypes:
  - Oxygen, Nitrogen and NitrousOxide: gas mode with `Evaporate`. Cryo puddles boil off, limited by the energy
    available, at up to `Rate`.
  - A short list of volatile unlinked reagents where boiling to smoke matters: WeldingFuel, Ethanol, Chlorine,
    Ammonia, SpaceCleaner.
  - Water already has it (`_KS14/Reagents/Partials/water.yml`); add `Evaporate` there.
  - Don't attach it to all ~60 volatile reagents; metals and salts never boil in a puddle.
- **Validation test**: for every reagent that is both Fluidmos-linked and has a `BoilTileEffect`, the effect's
  effective moles per unit, latent heat and gas must equal Fluidmos's. Otherwise cycling water between a puddle and a
  pipe creates or destroys matter or energy.

### Condensation: tile → puddle

Only linked gases condense; unlinked vapor on tiles exists only as smoke.

- Add a gas reaction, `KsCondensationReaction` (an `IGasReactionEffect`), modelled on `UpdatePuddleEvaporationReaction`.
  - One `gasReaction` prototype per linked gas, with `minimumRequirements` on that gas. Its `maximumTemperature` is
    that gas's van der Waals `Tc` (section 5c). Validate in a test that the YAML matches `8a / (27 R b)`.
  - Atmos already runs reactions only on active tiles that meet the requirements, so idle tiles cost nothing.
- When `p_gas > Psat(T) * (1 + SupersaturationMargin)`:
  - Remove `Δn` moles from the gas: the Hertz–Knudsen rate over 1 m² (the tile's floor), capped at `n - n_eq`.
  - Add the condensed units' latent heat (`LatentHeat` per unit, × `Δn / molesPerUnit`) to the tile, which warms it.
    Use the same accounting as `BoilTileEffect`, in reverse.
  - Queue `(grid, indices, units, T)` for the spill. The margin is about 5% and stops a puddle and a tile
    oscillating.
- Drain the queue in `Update`, outside atmos processing, and spill with `TrySpillAt` on the tile. It joins the tile's
  existing puddle, so there is still one puddle per tile.
- Results: steam vented into a cold room fogs and then leaves water on the floor, and cryo leaks form liquid
  oxygen and nitrogen puddles.

### Sub-boiling evaporation: linked reagents become humidity

With the mass scale (section 1), a saturated 293 K tile holds about 52u of water as vapor, so puddles and humidity
work at the same scale.

**Linked reagents** (Water, cryo O2, N2 and N2O puddles) evaporate into tile gas with the Hertz–Knudsen rate, as
`BoilTileEffect`'s `Evaporate` mode.
- The interface area is the puddle's tile coverage, `min(1, volume / AirHeatExchangeReferenceVolume)` m², the same
  coverage the heat exchange uses.
- Evaporation takes latent heat from the puddle solution.
- It conserves mass, and replaces upstream `EvaporationSpeed` for linked reagents only. Skip them in
  `GetEvaporationSpeeds` (`SharedPuddleSystem.Evaporation.cs`) with a KS marker.
- **Calibrate `α_puddle`** so water on a dry 293 K tile at 101 kPa evaporates at its current upstream
  `EvaporationSpeed`. Normal rooms then dry puddles exactly as fast as today. Humid or sealed rooms dry slower, and
  hot or low-pressure rooms dry faster.
- The evaporin multiplier from `GetModifiedEvaporationRate` scales `α_puddle`, so that mechanic keeps working.
- **Verify** that default scrubber filters include WaterVapor. If they don't, humidity accumulates; condensation still
  caps it at saturation, but it should be removed.

**Unlinked reagents** keep upstream `EvaporationSpeed`: the matter is removed, not converted. Turning every ethanol
puddle into a trickle of smoke would be visual noise for nothing. That is the one place mass isn't conserved; say so
in the docs.

**Condensation** also uses the Hertz–Knudsen rate, by the same formula with the sign reversed. The 5% supersaturation
margin stays, to stop it oscillating against evaporation.

## 4b. Ice on tiles: frozen reagents as an ice entity

A puddle's solution has no phases, so frozen reagents live in a separate entity, `KsIceSheet`, one per tile. It is
modelled on `Resources/Prototypes/Entities/Objects/Misc/ice_crust.yml`.

### The entity (`Resources/Prototypes/_KS14/Fluidmos/Entities/ice_sheet.yml`)

- **Kept from `IceCrust`**:
  - anchored, `SnapgridCenter`, `drawdepth: FloorObjects`, `Clickable`
  - its non-hard `MidImpassable` fixture
  - `FrictionContacts` and `TileFrictionModifier` (0.05)
  - the glass-break melee sound
  - `Damageable` and `Destructible`
  - the `ice_crust.rsi` sprite
- **Not parented to `IceCrust`.** Its `Temperature` and `TemperatureDamage` model a temperature this entity doesn't
  have: here the temperature is the solution's.
- **Added**:
  - A `SolutionContainerManager` with an `ice` solution, with no max volume beyond a sanity cap.
  - `KsIceSheetComponent`, server-side, `[Access]`ed to `KsIceSheetSystem` and holding no extra state. It marks the
    entity and keys the tile cache.
- **Appearance**: tint the sprite by the solution's colour (`GetColor`), with alpha scaled by volume, through the
  usual appearance-data path. A thin rime and a thick sheet then read differently, and frozen blood is red.
- **Slipping** applies only above a minimum volume (about 2u), so frost alone isn't a slip hazard. Toggle the friction
  modifier on a volume threshold rather than every tick.
- **Destroyed**: spill the solution as a puddle at its own temperature, through `TrySpillAt`. Shards on a cold tile
  simply refreeze.
- Locale: `ent-KsIceSheet` in en-US and ru-RU.

### One per tile, cached like puddles

`KsIceSheetSystem` keeps a per-grid `Dictionary<Vector2i, Entity<KsIceSheetComponent>>`, maintained on component
startup and shutdown and on anchor changes. It mirrors `PuddleSystem.Klovn.PuddleTileCache.cs`, so lookups are O(1)
and no entity lookup ever runs per tick. `ResolveIceSheet(gridUid, indices)` spawns one if the tile has none.

### Freezing: puddle → ice

- Add it as a `FreezeTileEffect`, a sibling of `BoilTileEffect` under `Content.Shared/_KS14/TileEffects/Effects/`,
  running in the same tile-effect pass on the same context.
- When the puddle solution is below a reagent's `MeltingPoint`, freeze the sensible-heat deficit:
  `units = (Tm - T) * C / FusionHeat`, capped by the amount present and by a rate. It's energy-limited, so the puddle
  plateaus at `Tm` until it has frozen.
- Move those units, with their reagent data, into the tile's ice sheet at `Tm`, using `KsPhaseMath` for the heat.
  Queue spawns and transfers through the effect context's deferred removal, as `BoilTileEffect` does, then drain them
  outside the walk.
- **Who gets it**: add it via partial prototypes to Water, Blood and the common liquids, and to the cryo reagents
  (liquid O2 and N2 freeze around 55–63 K). There's no need to add it to all ~400 reagents.
- **Sublimers are skipped**: a reagent whose boiling point at the tile's pressure is below its `MeltingPoint`, like
  CO2 at 1 atm, never forms ice. CO2 keeps its existing `BoilTileEffect` with `Sublimate`.

### Ice ↔ air and ice → puddle

- **Heat**: in the existing 1 s pass in `PuddleSystem.Klovn.AirHeatExchange.cs`, also walk the ice-sheet cache with
  the same `ExchangeHeatWithAir` math and the same deferred-heat list. Make it a KS-marked call into a
  `KsIceSheetSystem` method; generalise the private helper to take an `Entity<SolutionComponent>` if needed. Where a
  tile has both a puddle and ice, conduct heat between them with a high conductance, using `KsThermalTransfer`.
- **Melting**: once the ice solution is above a reagent's `MeltingPoint`, melt the sensible-heat surplus, limited by
  energy. Spill the melted units onto the tile's puddle with `TrySpillAt`, at `Tm`. When the ice is empty, delete it
  with `QueueDel`.
- **Sublimation**, for linked reagents only: ice in low-pressure air loses mass by Hertz–Knudsen at `x = 1` into the
  tile gas, taking the latent heat from the ice. A spaced room's ice slowly vanishes.
- **Pipe solids**: when a deconstructed pipe spills its share, solids go into the tile's ice sheet, not the puddle.

### Performance

- The cache only holds tiles that actually have ice, and the heat pass costs the same per sheet as per puddle.
- Freezing rides the tile-effect pass, which already visits puddles. Nothing ticks on tiles without ice or puddles.

## 4c. Device ↔ tile interface (Server `KsFluidmosTileSystem`)

### `ReleaseToTile(EntityUid deviceUid, GasMixture released)`

Call it right before every Merge from a device into a tile mixture, as a KS-marked one-liner.

- It detaches `released.Fluid`.
- **Liquid** goes into a per-device buffer, `KsFluidmosTileBufferComponent`, a server component `[Access]`ed to the
  system. Once the buffer reaches `SpillThresholdUnits`, or the device shuts down, it becomes a `Solution` at the
  mixture temperature, spilled with `_puddleSystem.TrySpillAt(coordinates, solution, out _, sound: false)`.
- **Unlinked vapor** is buffered the same way. At `SmokeThresholdUnits` it goes to the tile's smoke: add to existing
  smoke on the tile if there is any, otherwise spawn the smoke prototype and call `StartSmoke`, with the spread scaled
  by the units.
- **Linked vapor** is already in `Moles`, so it merges as ordinary gas.

Call sites, all KS-marked: the vent pump release, passive vent, outlet injector, canister release to the environment,
gas tank release, unsafe unanchor leak, and the portable scrubber output. Canister or tank destruction or rupture
releases all of its fluid. Check the KS `GasPiston` and `GasGrenadeCompressor` too.

### `AbsorbFromTile(EntityUid deviceUid, GasMixture destination, float fraction)`

Call it after the tile→pipe Remove in siphoning devices.

- **Smoke** goes to the vent scrubber (scrubbing or siphoning) and the portable scrubber. Take `fraction` of the smoke
  solution as liquid droplets; the phase step re-evaporates the volatile reagents. It's controlled by a
  `KsScrubAerosols` field on the scrubber component, default true; a UI toggle is a follow-up.
- **Puddles** go to vent pumps in siphon mode. Drain up to `PuddleDrainRate` u/s from `TryGetPuddle` on the device's
  tile through the solution container API.

## 5. Containers and new devices

- **Canisters**:
  - `Air` is already a DataField, so a mapper can prefill one with YAML `fluid: reagents: [...]`.
  - Add a `KsFluidmosCanisterWater` example prototype under `_KS14/Fluidmos/`.
  - Add fluid entries to the canister BUI state (KS-marked field) and list them in the window.
- **Gas tanks**: internals draws use `includeLiquid: false` and exclude vapor, so nobody inhales fluid. The release
  valve goes through `ReleaseToTile`.
- **`KsFluidPort`**: a unary atmos device under `Server/_KS14/Fluidmos/Devices/` and `Shared/_KS14/Fluidmos/`.
  - It holds a `fluidport` solution (300u) with Refillable, Drainable and Examinable solution components, so beakers,
    jugs and plumbing can work with it.
  - **Inject** mode moves solution into pipe liquid at a set u/s, with the energy-weighted temperature merge.
  - **Extract** mode moves pipe *liquid only* into the solution, preserving each reagent's data.
  - Toggled with an `AlternativeVerb`. Uses `AtmosDeviceUpdateEvent`.
  - **Plumbing (maintainer decision): the port is the only bridge between Fluidmos and Starlight's plumbing.** Pipes and
    plumbing stay separate models; plumbing tanks and reactors get no pipe connection. The port has a `plumbing` node
    (`TNorth`) beside its pipe node, and a `PlumbingInlet` and `PlumbingOutlet` on the `fluidport` solution and that node.
    Injecting, the inlet fills the tank from the network and the outlet is disabled; extracting, the outlet is enabled
    and a `PlumbingPullIntoAttemptEvent` handler refuses every inbound pull. So it never pulls from itself. Only liquid
    crosses. No Starlight code is edited.
  - **Plumbing keeps reagent data** (maintainer decision). Starlight's pulls used to convert every reagent to its generic
    form; `PlumbingPullSystem` now moves each data variant as itself, and the reactor and smart fridge count a reagent by
    prototype, every variant included. So blood keeps its DNA through plumbing → pipe → plumbing.
- **`KsPhaseSeparator`**: a trinary device with an inlet, a gas outlet and a liquid outlet.
  - The gas side uses the `PumpGasTo` pressure-target logic on gas and vapor, with `includeLiquid: false`.
  - The liquid side moves liquid with `RemoveLiquidRatio` at a set u/s.
  - Together with a heater, it makes distillation possible.
  - Sprites: reuse filter sprites with a tint until there's art.
- **Gas analyzer**: add a KS-marked `KsFluidEntry[]? Fluid` (prototype id, solid u, liquid u, vapor u) to the analyzer's
  `GasMixEntry`, and render it in the window. All strings localised (en-US and ru-RU).
- Both new entities need `ent-` entries in en-US and ru-RU.

## 5b. Phase separation, distillation and refrigeration

**Model limits.**
- Each pipe net is one well-mixed node: a single temperature, with liquid and vapor spread evenly.
- A transfer takes liquid in the same ratio as gas, weighted by `LiquidPseudoMolesPerUnit`. Only devices that ask for
  a phase separate the phases.
- Solids never transfer.
- There is no gravity and no stratification inside a net. With one temperature per net, "the cold solid stays
  behind while the warmer liquid escapes" can't happen within a net; it happens across nets. Pumping a 273 K slurry
  leaves the ice in the source net, and the liquid in the destination net then equilibrates there.

**`KsPhaseSeparator`** (section 5) is the separation primitive.
- Its gas outlet takes gas and all vapor (`includeLiquid: false`), using pressure-pump logic.
- Its liquid outlet takes liquid only (`RemoveLiquidRatio`), at a set u/s.

**Distillation emerges from what's already planned.**
- Heat the feed net, for example with a heat exchanger or thermomachine, then separate it.
- With Raoult's law, the vapor is enriched in the volatile component. At 360 K, ethanol's `Psat` is about 140 kPa and
  water's about 62 kPa.
- Chaining separator → condenser loop → separator gives a multi-stage column. Players build stills and fractionating
  columns out of plain atmos parts.

**Refrigeration and expansion valves emerge too.**
- Throttling a liquid near saturation flashes part of it to vapor, and that latent heat chills the low side. That is
  how real refrigeration works, and the phase step reproduces it. Any passive gate or pressure regulator works as a
  rough expansion valve. `KsExpansionValve` (5c) adds a liquid-only intake, an opening control and a readout.
- Throttling gas also cools it, through Joule–Thomson (5c). That makes refrigerant-free liquefaction possible too.
- The cycle:
  1. A pump compresses vapor, driving `p > Psat(T_ambient)`.
  2. It condenses in a radiator net and dumps its latent heat.
  3. The liquid crosses the valve.
  4. It evaporates in a heat-exchanger net at low pressure, so `Psat(T)` meets `p_low` at a low `T`.
- The refrigerant is any linked reagent with a convenient boiling point. Ammonia (240 K) is the classic; NitrousOxide
  (185 K) works with today's links.

**Second law: pumps heat what they compress (settled).**

Without this, atmos pumps do no thermodynamic work, and a refrigeration loop is free cooling.

**Applies to every pump transfer, not only fluid-bearing ones.**
- A loop's compressor often moves pure linked vapor, with `Fluid == null` because it has all evaporated, so a
  "carries fluid" condition would miss exactly the case that matters.
- Oxygen and nitrogen are linked anyway, so the narrower condition would cover nearly every pipe.

**Work: adiabatic compression.**
- Use real compressor discharge temperatures rather than isothermal work:
  - `T2s = T1 * (P_out / P_in)^((γ - 1) / γ)` is the ideal, reversible discharge temperature.
  - The real rise is `ΔT = (T2s - T1) / η`, with compressor efficiency `η` from `KsCCVars.AtmosPumpEfficiency`,
    default 0.7.
- Apply it only when `P_out > P_in`. Floor `P_in` at 1 kPa, so pumping from near-vacuum doesn't produce a huge ratio.
- **`γ` comes from atmos's own heat capacity**: `γ = 1 + R / Cv`, where `Cv` is the packet's unscaled molar heat
  capacity, `GetHeatCapacity(applyScaling: false) / TotalMoles`.
  - This handles mixtures automatically (`Cv` is already mole-weighted), and it matches the energy atmos stores
    (`E = C·T`), so compression adds heat on the same scale atmos accounts it in.
  - **Don't read upstream `GasPrototype.HeatCapacityRatio`, and don't add a Fluidmos γ field.** Upstream's values
    contradict upstream's own heat capacities. Plasma has `molarHeatCapacity: 200` (γ ≈ 1.04 implied) but
    `heatCapacityRatio: 1.7`. At 1.7, filling a canister heats each plasma packet by about 1580 K, far past plasma's
    373 K ignition point (`PlasmaMinimumBurnTemperature`). Leave the upstream field as it is; Fluidmos just doesn't
    use it.
  - Derived values: O2 1.42, N2 1.28, CO2 1.28, WaterVapor 1.21, N2O 1.21, Plasma 1.04 (tritiated ethanol's real
    value is about 1.13), Frezon 1.01 (barely heats when compressed, which suits a coolant).
  - **Clamp γ to at most 5/3**, the monatomic limit. Upstream's tritium (`molarHeatCapacity: 10`) is below the
    physical floor of 1.5R and would otherwise give 1.83.
  - **Tritium override**: give gases an optional `ksHeatCapacityRatio` in the `GasPrototype.Klovn.Thermo.cs` partial,
    used in place of the derived value. Set it only for Tritium, at 1.40 (real T2, diatomic), in the gas partial
    prototype. Mix an override into a mixture by converting it to `Cv = R / (γ - 1)`, then mole-weighting as usual.
- Raise the packet's temperature by `ΔT * KsCCVars.AtmosPumpCompressionHeatFraction`, before it merges, so the
  merge's heat-capacity mix spreads it into the output.
  - `SERVERONLY` cvars, defaults 0.7 and **0.1** (changed from 1.0 after playtesting: pumped gas ran far too hot).
  - **The fraction never destroys heat.** What the gas doesn't keep goes into the pump's tile air, as an aftercooler
    would shed it, so refrigeration still pays its full pump work (no super-Carnot fridge feeding a TEG). With nowhere to
    shed it (vacuum, no device), it all stays in the gas. Tests of the physics itself override the fraction to 1.
  - Cost: one `MathF.Pow` per pump transfer.
- A filled canister now comes out about as hot as a real compressor would make it. That is hotter than isothermal
  work by design, and the efficiency cvar is the balance knob.
- Pumping downhill (`P_out ≤ P_in`) does no compression work. The gas still throttles, so Joule–Thomson applies
  (5c).

**Helper and call sites.**
- This is the compression half of `KsTransferThermo.Apply(packet, inPressure, outPressure, doesWork: true)`, defined
  in 5c. There is one helper, not two.
- Call it with a KS-marked line in:
  - the shared `PumpGasTo` helper
  - `GasPressurePumpSystem`, `GasVolumePumpSystem` and `GasRecyclerSystem`
  - `GasVentPumpSystem` and `GasVentScrubberSystem` (both directions), and `PortableScrubberSystem`
  - KS `MolarMixerSystem` and `GasPistonSystem`
- Grep `Content.Server/Atmos/Piping` and `_KS14/Atmos` for anything else that moves gas uphill.

**Balance impact (deliberate; it applies to all atmos).**
- Filling a canister from 101 to 4500 kPa with N2 at 293 K: `T2s = 293 * 44.5^(0.4/1.4) ≈ 866 K`, so the packet
  rises about 820 K at `η = 0.7`. The packets mix into the canister's existing contents, so the canister warms less
  than that, but filled canisters still come out hot and need cooling.
- `TEG` loops and burn chambers gain heat from their pumps.
- **Plasma is the station's fuel, so check it first.**
  - Pure plasma at γ 1.04 rises about 70 K per packet when filling a canister, which stays below its 373 K ignition
    point.
  - **Premixes are a different matter.** Oxygen raises the mixture's γ. A 50/50 plasma–oxygen premix compressed from
    101 to 4500 kPa rises about 130 K, reaching about 420 K, and ignites in the pipe. That is compression ignition,
    as in a diesel engine, and it's physically right. Only mixes of roughly 85% plasma or more stay below ignition at
    that ratio. Small ratios are safe: a 50/50 premix pumped from 101 to 300 kPa rises about 33 K.
  - Tritium–oxygen premixes ignite even more readily (tritium's γ is 1.40).
  - These are balance calls for playtesting. Don't special-case fuels in code.
- The cvar fraction is the balance knob if playtests show the change is too sharp.

**Result.** The vapor-compression loop now obeys `Q_hot = Q_cold + W`. Cooling costs pump work, which costs pump
power upstream.

## 5c. Throttling: Joule–Thomson, flash and liquid flow (settled: Joule–Thomson is included)

Throttling is gas or liquid crossing a pressure drop with no work done. It isn't specific to valves; three of the four
pieces are general.

| Piece | Where it applies | Implemented |
| --- | --- | --- |
| Joule–Thomson cooling or heating of gas | Every throttling transfer | Once, in a shared helper |
| Flash of liquid above its local boiling point | Every net and every packet | Once, in the phase step |
| Orifice flow law for liquid | Every *passive* device that liquid crosses | Once, in a shared helper; each device opts in |
| Liquid-first intake, opening control, vapor-fraction readout, constant-pressure mode | The expansion valve only | `KsExpansionValve` |

### Joule–Thomson (general)

**Data.** Add van der Waals constants to gases through a `GasPrototype.Klovn.Thermo.cs` partial: `KsVanDerWaalsA`
(kPa·L²/mol²) and `KsVanDerWaalsB` (L/mol). The values go in a gas partial prototype, `!PartialOnly gas`, under
`_KS14/Fluidmos/Partials/`.

| Gas | a (kPa·L²/mol²) | b (L/mol) | Source |
| --- | --- | --- | --- |
| N2 | 137 | 0.0387 | real |
| O2 | 138 | 0.0319 | real |
| CO2 | 364 | 0.0427 | real |
| WaterVapor | 554 | 0.0305 | real |
| N2O | 383 | 0.0442 | real |
| Ammonia | 425 | 0.0371 | real |
| Tritium | 24.8 | 0.0266 | hydrogen's values: inverts at about 200 K, so it **heats** on throttling at room temperature |
| Plasma | 1256 | 0.0871 | ethanol's real values (lore: tritiated ethanol). `Tc` about 514 K and inversion at about 3470 K, so it cools strongly on throttling, about 20 K when bled from 4500 to 101 kPa |
| Frezon | 600 | 0.050 | fictional coolant: the strongest Joule–Thomson cooling of any gas, which suits its role |
| Any other gas, including KS-only gases | none | none | ideal: `μ = 0` |

The inversion temperature of the van der Waals model is `2a / (R b)`. Use it to sanity-check any new gas's constants.
A gas without constants is ideal, so it neither cools nor warms on throttling.

There is no γ table: γ is derived from heat capacity (section 5b). Only Tritium has a `ksHeatCapacityRatio` override
(1.40).

- Cache the values in arrays indexed by gas, as atmos does with `GasSpecificHeats`: `a`, `sqrt(a)`, `b`, and each
  gas's `Cv` (the override already converted). Build them in a shared system, rebuilt on prototype reload, in the
  same way as `KsFluidmosPropertiesSystem`.
- **Validation test**: every gas with constants has `a ≥ 0`, `b > 0`, and an inversion temperature above its critical
  temperature. Every gas's effective γ is in (1, 5/3].

**Scope: van der Waals is not the equation of state.**
- Pressure stays ideal-gas everywhere. The constants enter only through the second virial coefficient,
  `B(T) = b - a/(RT)`, which is the low-pressure limit of van der Waals.
- From that come two things:
  1. the Joule–Thomson coefficient at throttles (below);
  2. each linked gas's critical temperature, `Tc = 8a / (27 R b)` (N2: 126 K, against a real 126.2 K).
- **`Tc` is a hard cap on condensation.** Above it, no liquid forms at any pressure: the phase step won't condense,
  and the condensation reaction's maximum temperature is `Tc`. This replaces the "near critical point" guess in 4a and
  gives "no supercritical fluids" a physical boundary. Unlinked reagents keep the `Psat` exponent clamp.
- Vapor pressures still come from Clausius–Clapeyron, anchored at `BoilingPoint`. Van der Waals' own saturation curve
  is quantitatively poor, and solving its cubic per transfer would cost the hot path and change all atmos pressures.

**Math.** Put it in a shared helper, `Content.Shared/_KS14/Atmos/KsThrottling.cs`.
- Mixture constants use van der Waals mixing: `a_mix = (Σ x_i * sqrt(a_i))²`, `b_mix = Σ x_i * b_i`. That's one
  loop over the gas array, with `sqrt(a_i)` cached.
- `μ = (2 * a_mix / (R * T) - b_mix) / Cp_m` K/kPa. 1 kPa·L = 1 J, so `ΔT = μ * ΔP` comes out in kelvin. Check:
  nitrogen at 293 K gives 0.25 K/bar, against a real 0.22.
- Use the atmos molar heat capacity, unscaled (`GetHeatCapacity(applyScaling: false) / TotalMoles`), so the result
  agrees with atmos's own energy.
- Integrate drops over 500 kPa in 4 substeps, since `μ` depends on `T`. Apply the result to the moved **packet**,
  before it merges.
- Pressure stays ideal-gas everywhere. JT is only a thermal correction at the throttle; a full van der Waals equation
  of state would change all of atmos for nothing.
- **Energy accounting.** Real throttling conserves enthalpy. In atmos's ideal `E = C·T`, the JT temperature change
  shows up as energy appearing or disappearing: the real-gas internal energy atmos can't represent. That is acceptable
  because no free cycle exists. Adiabatic compression heat (section 5b) is far larger than the JT cooling over the
  same pressure ratio (about 820 K against about 10 K for N2 from 101 to 4500 kPa), so liquefying air costs pump
  work.

`KsTransferThermo` lives in `Content.Server/_KS14/Atmos/KsTransferThermo.cs`. It uses `KsThrottling` (shared) for the
JT math, and the gas-constant arrays for `γ`.

**Call sites.** One combined helper, `KsTransferThermo.Apply(packet, inPressure, outPressure, doesWork)`:
- With `doesWork` true (pumps) and `P_out > P_in`, it adds compression heat (section 5b).
- With `P_out < P_in`, it applies Joule–Thomson. That covers passive devices, and pumps moving gas downhill.

It replaces the separate compression-heat call sites, at the same places plus the throttles:
- `GasPassiveGateSystem` (l.44), `GasPressureRegulatorSystem` (l.143) and `PressureControlledValveSystem`
- The pumps listed in 5b
- Canister and gas-tank release
- Vent releases, where the tile is the outlet
- `AtmosUnsafeUnanchorSystem` leaks: a ruptured canister's jet comes out cold

**Not applied** to tile-to-tile flow (LINDA, Monstermos): the pressure differences there are small, it's the hottest
path in atmos, and spacing sends gas into the immutable space mixture anyway.

**Balance.** A canister of N2 bled from 4500 to 101 kPa cools by about 10 K. Hot burn-chamber exhaust above its
inversion temperature warms slightly. Hydrogen-like tritium warms on release. Liquefying air without any refrigerant
(the Linde cycle) becomes possible: compress, cool the high side in a radiator, throttle, recycle the cold vapor
through a heat exchanger, and repeat until liquid O2 and N2 condense into the phase separator.

### Flash (general, in the phase step)

Change phase step 3: **above `Tb(P_total)`, boiling is energy-limited and instant**, not rate-limited:
`units = (T - Tb(P)) * C / LatentHeat`, so the mixture lands at `Tb(P)`.
- This is `BoilTileEffect`'s formula, through `KsPhaseMath`. There is no separate boiling-rate constant.
- Hertz–Knudsen still governs evaporation and condensation below the boiling point.
- So any liquid entering a lower-pressure net (through a valve, gate, regulator or leak) flashes to the textbook
  vapor fraction and outlet temperature on that net's next `React`, independent of the `α` tuning.
- With non-condensable gas present, the total pressure is only an approximation. The Hertz–Knudsen terms then correct
  the partial pressures over the following steps.

### Orifice flow law for liquid (general, passive devices opt in)

- Without it, liquid crossing a passive device rides along with the gas ratio. A gate between two nets of pure liquid
  barely moves anything, because there is almost no gas pressure difference.
- New shared helper, `KsLiquidFlow.Transfer(inlet, outlet, conductance, dt)`: moves `Q = conductance * sqrt(ΔP)` u/s
  of liquid only (`RemoveLiquidRatio`), capped so it never reverses the pressure difference.
  - **Compute the cap from fill volume, not pressure**: move at most the liquid that would equalise the two nets'
    liquid fill fractions. Near the hydraulic floor a tiny volume change swings pressure by thousands of kPa, so a
    pressure-based cap overshoots and the two nets ping-pong every tick.
- The passive gate, pressure regulator and pressure-controlled valve call it in addition to their gas transfer. The
  conductance is a component `[DataField]`; the regulator scales it by its opening.
- Their gas transfer then uses `includeLiquid: false`, so liquid isn't moved twice.
- Pumps keep moving liquid with the gas through `LiquidPseudoMolesPerUnit`: they force flow, so the orifice law
  doesn't apply to them.

### `KsExpansionValve` (the only valve-specific part)

A binary device, under `Server/_KS14/Fluidmos/Devices/` and `Shared/_KS14/Fluidmos/`.
- **Liquid first** (`RemoveLiquidRatio`), modelling a valve fed from the bottom of a condenser receiver. Whatever
  of the orifice the liquid leaves passes vapor, by mass at `sqrt(rho_vapor / rho_liquid)` of the liquid conductance,
  JT-throttled and capped at the pressures meeting. A liquid-only valve stalled a loop whose condenser hadn't yet
  delivered liquid (phase 6 cycle test).
- **Opening**, 0–100%, which scales the orifice conductance. Set from the UI.
- **Readout of the outlet vapor fraction**: compute the same flash on a copy of the packet, for display only. The real
  flash happens in the outlet net's phase step.
- **Optional automatic (constant-pressure, AXV) mode**: each update, move the opening towards holding the outlet net at
  `PressureSetpoint`, which sets the evaporator's boiling point. Replaced the planned thermostatic (TXV) mode in phase 6:
  every radiator splits an evaporator into its own nets, so the valve never sees the evaporator outlet a TXV bulb reads,
  and a TXV reading its own outlet sees only flash at `Tb(P)` and shuts for good.
- **Sized for a volume pump**: `conductance: 40` in YAML, four times the passive-device default.
- Locale: `ent-` entries in en-US and ru-RU, plus the UI strings.

## 6. Performance checklist

- Tiles: one null check each in `Pressure`, `GetHeatCapacity`, `Merge`, `RemoveRatio` and `React`. Nothing else.
  Compare before and after with `AtmosphereSystem.BenchmarkHelpers.cs` and the atmos benchmarks.
- Fluid-less pipes pay the same. Emptied fluid is nulled during the phase step.
- No prototype lookups or LINQ in split, merge or transfer. Property lookups happen only in the phase step and at the
  Solution boundary.
- Caches are incremental, with a periodic recount. The phase step skips quiescent mixtures.
- `KsReagentDataSet` instances are shared by reference, so key equality is a reference compare in practice.
- Tile I/O happens only at device update points, buffered into spills and smoke releases above a threshold.

## 7. Out of scope (follow-ups)

- A reagent mode on `GasFilter`.
- Showing liquid in pipe visuals.
- Freezing-point depression in mixtures, and pipes fully blocking flow when frozen solid (beyond the
  hydraulic-pressure clog). Tile freezing is covered by the ice sheet in 4b.
- **Second phase** (the planned follow-up, not this work): Henry's-law gas solubility, Margules activity coefficients (azeotropes), and chemical
  reactions inside pipe networks (throttled, only on fluid-bearing networks whose composition changed).
- **Separate project**: finite-volume pipes (a cell per pipe node, with friction-driven flow). Judge it against a
  profile of a real station's pipe count.
- Supercritical fluids. Linked gases stop condensing above their van der Waals `Tc`; unlinked reagents keep the
  `Psat` exponent clamp.
- Chemical reactions between solids and liquids at an interface.
- A `KsFluidMixture` inside a tile's `GasMixture`. By design that never exists: **a tile's fluid is its single puddle**
  (section 4a), which exchanges with the tile's gas through `BoilTileEffect` and the condensation reaction, plus smoke
  for unlinked vapor.

- **Phase changes in plumbing.** Plumbing Solutions have no phase step: hot pipe liquid pulled into plumbing stays liquid at
  its temperature. Accepted.

## 8. Build order

Each phase ends green before the next starts. "Green" means:
- `dotnet build -c Release` with no `error CS`.
- That phase's tests, plus the existing `_KS14` atmos, firefighting and puddle tests, under `-c Debug`.
- `SandboxTest` and `UninitializedSaveTest`.
- Every new test seen failing once with its mechanism broken.

Commit per phase. Phases 0 and 4 change existing gameplay; say so in their commit messages.

| Phase | Contents | Sections | Behaviour change for players |
| --- | --- | --- | --- |
| **0** | Kelvin standardisation: YAML, the readers, the units test | 0 | None. The values mean the same temperatures. |
| **0a** | Plasma and tritium reagent boiling and melting points. Apply as its own commit whenever it's reached, even after later phases. | 0a | Minor. Ground plasma is a liquid in pipes, and liquid tritium vaporises. |
| **1** | Core data: constants, the reagent partial and seed data, `KsPhaseMath`, the properties system, the data set, keys, `KsFluidMixture` and its serializer. Then `GasMixture` integration (the `Fluid` field, `FluidCapable`, `FreeVolume`, `TransferMoles`, `Pressure`, linear ops) and `Merge`, `DivideInto`, heat capacity, guards and capability marking. | 1, 2 | None. No fluid exists yet; tests inject it. |
| **2** | Phase step: Hertz–Knudsen, flash, freezing and melting, sublimation, the `Tc` cap. Refactor `BoilTileEffect` onto `KsPhaseMath`, with identical behaviour at 101 kPa: run the firefighting and CO2 tests before and after. | 3, 4a (refactor only) | None until fluid exists. |
| **3** | Tile interface: `ReleaseToTile` and `AbsorbFromTile` at every call site, the condensation reaction, the `BoilTileEffect` `Smoke` and `Evaporate` modes and pressure-dependent boiling point, the evaporation-speed skip for linked reagents, `KsIceSheet` and `FreezeTileEffect`. | 4a, 4b, 4c | **Yes.** Water puddles dry into humidity, spaced puddles boil, and steam condenses. |
| **4** | Thermodynamics of transfers: the gas constants partial and data, `KsThrottling`, `KsTransferThermo` at every pump and throttle, the cvars, `KsLiquidFlow` in passive devices. | 5b, 5c | **Yes, atmos-wide.** Pumps heat their output, and throttled gas cools. |
| **5** | Devices and UI: `KsFluidPort`, `KsPhaseSeparator`, `KsExpansionValve`, the canister example and UI, the analyzer UI, en-US and ru-RU locale. | 5, 5c | New content. |
| **6** | Calibration and performance: tune `α` and `α_puddle` against their tests, then run the atmos benchmarks before and after phases 1–5. Playtest the pump-heat cvars. | 3, 4a, 6 | Tuning only. |

### Recorded for phase 6 and the guidebook (maintainer decisions, no code yet)

- **`KsFluidmosCycleTests`** (done), every loop with a **volume pump** as the compressor, not a pressure pump: a pressure
  pump has no displacement limit and drains the suction to a few kPa.
  - `AmmoniaLoopCoolsASealedRoom`: condenser under outdoor air, 293 K to about 234 K in ten minutes.
  - `NitrousOxideLoopInSpaceCoolsASealedRoom`: condenser radiating to space at about 5 MPa, 293 K to about 194 K.
  - `LoopEnergyBalances`: pump, condenser and evaporator each in a sealed room; the first law holds within the
    Joule–Thomson residual (about 6% of what the refrigerant carries).
  - `LindeLoopLiquefiesNitrogen`: an eight-chamber counterflow exchanger (one-tile sealed chambers of plasma, a radiator
    from either stream in each) and an expansion valve as the throttle; liquid nitrogen after about 48 minutes. A passive
    gate can't throttle (it only equalises), and the phase separator can't sit after the throttle (it pumps like a
    pressure pump, draining the throttle's outlet and compressing the return).
  - The flash fraction is `KsFluidmosPhaseTests.LiquidEnteringALowPressureNetFlashesToItsBoilingPoint`, and Raoult
    distillation `KsFluidmosNewDeviceTests.PhaseSeparatorDistilsAnEthanolEnrichedVapor`.
- **Phase 6 calibration and benchmarks** (done):
  - The pipe alpha now has a two-sided test: water reaches 90% of saturation in about 3 s (between 2 and 5). The
    puddle alpha was already pinned within 10% of upstream's rate.
  - Benchmarks, Release, BenchmarkDotNet medium job, the branch's base commit `9967ebe2d23` against phase 6:
    - `KsAtmosStationBenchmark` (new, upstream API only; ten full atmos cycles on Box): 7.88 ± 1.29 ms before,
      7.57 ± 0.31 ms after. No measurable change; allocations 3.56 to 3.91 MB per call (+10%).
    - `GasReactionBenchmark`: every reaction within 3% (plasma and tritium fires +2.7%, the rest within 1%).
    - `HeatCapacityBenchmark`: server 85.0 to 81.8 us, client 87.0 to 91.3 us (+5%).
  - Not done here: playtesting the pump-heat cvars, which needs a person in game.
- **Phase 6 radiator changes** (found building the loop; `KsRadiatorFluidTests`):
  - Radiators condense and boil the packet passing them at line pressure (`KsFluidmosPhaseSystem.ExchangeAtWall`), the
    latent heat going to their tile, bounded by `KsLatentHeatTransferScale` (1/64) of their heat transfer. Unbounded,
    a condenser condenses at ambient exactly and the chain's pressure drop re-boils its liquid before the valve.
  - Radiators take their packet by the gas share, sweeping the same share of liquid, instead of `Remove(n)` by
    `TransferMoles`; liquid-laden nets choked the flow.
  - An immutable heat sink with moles (planet air, lattice) takes a pump's shed compression heat; vacuum still doesn't.
- **Guidebook** (done: `KsLiquidsInPipes`, `KsFluidDevices`, `KsRefrigeration`, en-US and ru-RU, under Atmospherics).
  The notes it covers:
  - A compressor is a volume pump when the refrigerant needs more than 4500 kPa.
  - Condensers must be below the refrigerant's Tc.
  - No device moves heat directly between two pipe nets: put both sets of radiators in a small sealed chamber.
  - `AmmoniaOxygenReaction` runs above 323 K, so a compressed ammonia loop with oxygen in it burns. Keep refrigerant
    loops oxygen-free.
  - Pumps keep only `pump_compression_heat_fraction` (default 0.1) of their compression heat in the gas; the rest warms
    the pump's room.

## 9. File map

**New files.** Fork-owned, so no provenance markers.

| Path | Contents |
| --- | --- |
| `Content.Shared/_KS14/Fluidmos/KsFluidmos.cs` | Constants |
| `Content.Shared/_KS14/Fluidmos/KsPhaseMath.cs` | `Psat`, `Tb(P)`, moles per unit, latent and fusion heats |
| `Content.Shared/_KS14/Fluidmos/KsFluidMixture.cs`, `KsFluidReagentKey.cs`, `KsFluidAmount.cs`, `KsReagentDataSet.cs` | Data |
| `Content.Shared/_KS14/Fluidmos/Serialization/KsFluidReagentsSerializer.cs` | YAML |
| `Content.Shared/_KS14/Fluidmos/KsFluidmosPropertiesSystem.cs`, `KsFluidReagentProperties.cs` | Property table |
| `Content.Shared/_KS14/Fluidmos/KsFluidEntry.cs` | UI data |
| `Content.Shared/_KS14/Atmos/KsThrottling.cs`, `KsGasThermoSystem.cs` | JT math; gas-constant arrays |
| `Content.Shared/Atmos/GasMixture.Klovn.Fluid.cs` | `GasMixture` partial |
| `Content.Shared/Atmos/Prototypes/GasPrototype.Klovn.Thermo.cs` | Gas constant fields |
| `Content.Shared/Chemistry/Reagent/ReagentPrototype.Klovn.Fluidmos.cs` | The `fluidmos:` block |
| `Content.Shared/_KS14/TileEffects/Effects/FreezeTileEffect.cs` | Puddle → ice |
| `Content.Server/_KS14/Fluidmos/KsFluidmosPhaseSystem.cs` | Phase step |
| `Content.Server/_KS14/Fluidmos/KsFluidmosTileSystem.cs`, `KsFluidmosTileBufferComponent.cs` | Device ↔ tile |
| `Content.Server/_KS14/Fluidmos/KsCondensationReaction.cs` | Tile → puddle |
| `Content.Server/_KS14/Fluidmos/Ice/KsIceSheetSystem.cs`, `KsIceSheetComponent.cs` | Ice |
| `Content.Server/_KS14/Fluidmos/Devices/` | Fluid port, phase separator, expansion valve: systems and components |
| `Content.Shared/_KS14/Fluidmos/Devices/` | Their shared components and UI messages |
| `Content.Client/_KS14/Fluidmos/` | Device windows, and the fluid lists for the canister and analyzer |
| `Content.Server/_KS14/Atmos/KsTransferThermo.cs`, `KsLiquidFlow.cs` | Transfer thermodynamics |
| `Content.Server/Fluids/EntitySystems/PuddleSystem.Klovn.Fluidmos.cs` | Puddle-side hooks, if any are needed beyond tile effects |
| `Resources/Prototypes/_KS14/Fluidmos/Partials/{reagents,gases}.yml` | Partials; register the directory in `KsEntryPoint` |
| `Resources/Prototypes/_KS14/Fluidmos/Entities/{ice_sheet,devices,canisters}.yml` | Entities |
| `Resources/Prototypes/_KS14/Fluidmos/reactions.yml` | Condensation reactions |
| `Resources/Locale/{en-US,ru-RU}/_KS14/Fluidmos/*.ftl` | Locale |
| `Content.IntegrationTests/Tests/_KS14/Fluidmos/*.cs` | Tests |

**Edited upstream files.** Every change gets a `KS14` marker.
- `GasMixture.cs`
- `SharedAtmosphereSystem.Gases.cs`
- `AtmosphereSystem.Gases.cs`
- `PipeNet.cs`
- The canister and gas tank components and systems
- The device systems listed in 4c, 5b and 5c
- `SharedPuddleSystem.Evaporation.cs`, for the linked-reagent skip
- `PuddleSystem.Klovn.AirHeatExchange.cs`, for the ice heat call
- The analyzer's `GasMixEntry` and window
- `ExtinguishTileReaction.cs`
- The reagent YAML (section 0)

`KsCCVars` gets `AtmosPumpEfficiency` and `AtmosPumpCompressionHeatFraction` (`SERVERONLY`).

## 10. Repo traps that apply here (from `CLAUDE.md`)

**Skills to load first:**
- `integration-testing` before writing any test: pool settings, server/client driving, and telling real failures from
  pool noise.
- `ui` before the device windows and the canister and analyzer fluid lists.
- `sprites` before the ice sheet's tint and alpha appearance.
- `guidebook`, if a Fluidmos guidebook page is added. Optional, but players will need one to discover distillation,
  refrigeration and liquefaction.

- **Pooled test servers share a process.** No `static` mutable state anywhere, including the property tables, the
  gas-constant arrays and the ice cache: instance fields on systems only.
- **Sandbox.** No `CollectionsMarshal`, `stackalloc`, `Unsafe`, `u8` literals, or collection expressions that target
  `List<T>`. Run `SandboxTest`; ordinary tests don't check this.
- **One subscription per component and event pair.** Puddle startup and shutdown and anchor events are likely already
  taken. Call into the ice cache from the existing handlers rather than subscribing again, and the same for
  `ComponentStartup` and `ComponentShutdown` on reused components.
- **Clean up on `ComponentShutdown`**, not `EntityTerminatingEvent`. That applies to the ice cache and the tile
  buffers, which flush their spill when their device shuts down.
- **Never spawn or delete during an engine walk.** The condensation spills, ice spawns and melting spills are all
  queued and drained in `Update`, as the puddle heat exchange already does. Use `QueueDel` for emptied ice.
- **`Resolve` logs an error when a component is missing.** Pass `logMissing: false` where it's legitimately optional.
- **`EntityQueryEnumerator` skips paused entities.** Decide per loop.
- **Typed prototype ids only**, including in tests (`RA0033`). A test-only id goes in a non-static field.
- **Name every optional argument**, and use `Entity<T>` rather than a uid and a component side by side.
- Subscribe with `[SubscribeLocalEvent]`, and use `Subs.CVar` for the cvars.
- **`[Access]`** on `KsIceSheetComponent`, `KsFluidmosTileBufferComponent` and the device components.
- **Map saves keep only `[DataField]`s.** `FluidCapable` and the caches are deliberately not saved. `GasMixture.Fluid`
  is saved. `UninitializedSaveTest` must stay green.
- **Tests must not copy tuning numbers.** Derive expected values from prototypes and constants, as the verification
  list says.
- **Debug and Release catch different things.** Build Release for warnings-as-errors and test Debug for assertions.
  The `Merge` safety assert only fires in Debug.

## Verification

Put tests in `Content.IntegrationTests/Tests/_KS14/Fluidmos/`. Use typed `ProtoId` fields, and derive expected
values from the constants and prototypes rather than copying numbers into the tests.

- **`KsFluidMixtureTests`**:
  - Ratio, merge, divide and clone conserve the liquid and vapor totals.
  - Blood with different DNA stays as separate keys and survives a Solution→fluid→Solution round trip.
  - The incremental caches equal a recount.
  - The map serializer round-trips.
- **`KsFluidmosPhaseTests`**:
  - A heated closed canister of water ends at `n_eq` within the step tolerance, and boiling cools the mixture.
  - WaterVapor gas in a cold pipe condenses into Water liquid.
  - Raising pressure with N2 slows water evaporation compared with a control.
  - Overfilling with liquid raises the hydraulic pressure and the pressure pump stops.
- **`KsFluidmosDeviceTests`**:
  - For every tile-facing device: liquid reaches a puddle and unlinked vapor reaches smoke. The `Merge` safety assert
    must not fire, so run under Debug.
  - The scrubber absorbs smoke, and a siphoning vent drains a puddle.
  - The fluid port round-trips reagent data.
  - Phase-separator outputs.
  - Gas-tank internals deliver no fluid.
  - Splitting a pipe net and reconnecting it conserves fluid.
- **`KsPuddlePhaseTests`**:
  - A hot water puddle boils down to `Tb(P)` and adds the matching WaterVapor moles to its tile.
  - The same puddle on a low-pressure tile boils at a lower temperature.
  - A boiling unlinked reagent makes smoke.
  - Supersaturated steam on a cold tile leaves a puddle, and the tile keeps only one puddle.
  - No condensation within the supersaturation margin, so nothing oscillates.
- **`KsFluidmosCycleTests`**:
  - A separator splits a heated ethanol/water feed with the vapor ethanol-enriched; derive the expected ratio from
    Raoult's law.
  - A throttled NitrousOxide loop chills its low-side net below ambient.
  - The loop's energy balance holds: the heat its condenser dumps equals the heat its evaporator absorbs plus the pump
    work.
  - Pumping into a higher pressure raises the output temperature by `W / C`.
- **Ice sheets (4b)**:
  - A water puddle on a tile held below 273 K freezes at a plateau into one `KsIceSheet`, with no second sheet, and
    the fusion heat is accounted for.
  - Blood keeps its DNA data through freeze and melt.
  - Warming the tile melts it back into the tile's puddle.
  - Ice in vacuum sublimes into tile WaterVapor.
  - Breaking the sheet spills a puddle.
  - Slipping only applies above the volume threshold.
  - `UninitializedSaveTest` passes with the new prototype.
- **Adiabatic compression**:
  - Pumping from 101 to 4500 kPa raises the packet by `T1 * ((P2/P1)^((γ-1)/γ) - 1) / η`, derived from the gas's
    `γ`, the cvar and the efficiency, not from a copied number.
  - Every gas's effective γ equals `1 + R / Cv`, computed from its prototype's `molarHeatCapacity` (clamped to 5/3),
    or its `ksHeatCapacityRatio` override where set. Tritium's is 1.40.
  - Filling a canister with pure plasma from 101 to 4500 kPa leaves it below `PlasmaMinimumBurnTemperature`, and no
    plasma fire reaction runs.
  - Pumping a 50/50 plasma–oxygen premix over the same ratio ignites it in the pipe. Derive the threshold from `γ`;
    this pins the compression-ignition behaviour, so a later change that silently removes it fails.
- **Hydraulic pressure**:
  - A net 100% overfull with liquid reports a finite pressure, no greater than `MaxHydraulicPressure`.
  - A passive gate between two overfull pure-liquid nets settles without the flow direction flipping every tick.
- **Plasma and tritium reagents (0a)**: liquid plasma in a pipe stays liquid at 293 K and boils to smoke above 352 K.
  Liquid tritium vaporises. Neither creates plasma or tritium gas.
- **Throttling**:
  - Nitrogen bled through a passive gate from 4500 to 101 kPa cools by the integrated `μ * ΔP`; derive the expected
    value from the van der Waals constants.
  - A gas above its inversion temperature, and tritium at room temperature, warms instead.
  - A Linde loop of pumps, radiator, heat exchanger and gate eventually condenses liquid nitrogen.
  - Liquid entering a low-pressure net lands at `Tb(P)` with the flash fraction from the energy balance.
  - A gate between two pure-liquid nets moves liquid at `conductance * sqrt(ΔP)`.
  - An expansion valve in automatic mode holds its outlet pressure within tolerance, and passes vapor with no liquid.
- **Solids**:
  - Cooling water below `Tm` freezes it at a plateau, with the latent heat accounted for.
  - A pump drawing from a slurry moves only liquid.
  - Ice-packed nets raise the hydraulic pressure.
  - A solid in vacuum sublimes.
- **Hertz–Knudsen calibration**:
  - A pipe node reaches 90% of saturation within the time the `α` tuning targets.
  - A water puddle on a dry 293 K tile evaporates at its upstream `EvaporationSpeed`, within one pass's rate.
  - The same puddle in a saturated sealed room barely shrinks.
- **Prototype validation**: the `ByGas` links are unique, boiling points are positive, and linked gases exist.
- **Vapor-pressure curve** (`KsPhaseMathTests`):
  - `Psat(BoilingPoint)` is 101.325 kPa for every volatile reagent, and `Tb(101.325)` returns `BoilingPoint`.
  - Water's `Psat(293.15 K)` lands within 25% of the real 2.34 kPa. This is the regression test for deriving `k` from
    the game-scale latent heat, which gives about 43 kPa.
  - Every reagent's `k` is between 5 and 20, so a unit mix-up (`k` ≈ 0.17) fails here, not in play.
- **Prove each test fails**: break the mechanism, then put it back (`CLAUDE.md` §6).
- Build and run:
  - `dotnet build -c Release`
  - The `_KS14` tests with `-c Debug`
  - `SandboxTest` (new collections and serializers)
  - `UninitializedSaveTest` (canister and tank components)
  - The atmos benchmarks
- **In game**:
  - Pour water into a fluid port, pump it through a heater to a phase separator, and release the steam from a vent.
  - Check that WaterVapor appears on the tile, that ethanol vapor appears as smoke, and that a liquid-filled vent
    leaves a puddle.
