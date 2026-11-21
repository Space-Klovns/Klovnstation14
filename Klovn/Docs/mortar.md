# Mortar

A light mortar: an anchored structure that lobs shells at a bearing and distance. It is fired either by the NPC crewman
that set it up, which picks its own targets, or, with no crewman alive, by anyone through its window. It has a fixed
number of rounds and is never moved. Every shell is a supply pod spawned where it is to land, falling for the whole
flight with the ground under it marked, and going off as it lands.

| Piece | Where |
| --- | --- |
| `KsMortarComponent`, `KsMortarCrewComponent`, `KsMortarShellComponent`, `KsMortarDeployedEvent` / `KsMortarLostEvent` | `Content.Server/_KS14/Mortar/KsMortarComponent.cs` |
| `KsMortarSystem` (crew set-up and fire, manual fire, launch, abandoning, dissolving) | `Content.Server/_KS14/Mortar/KsMortarSystem.cs` |
| The sight: eyes, view subscriptions, `KsMortarSight` | `Content.Server/_KS14/Mortar/KsMortarSystem.Sight.cs` |
| Window: `KsMortarUiKey`, `KsMortarBuiState`, `KsMortarFireMessage`; `KsMortarAim` (bearing and distance to an offset and back) | `Content.Shared/_KS14/Mortar/KsMortarUi.cs`, `KsMortarAim.cs` |
| `KsMortarWindow`, `KsMortarAimControl` (the plot), `KsMortarSightReticle`, `KsMortarBoundUserInterface` | `Content.Client/_KS14/Mortar/` |
| `KsMortar`, `KsMortarShell`, `KsMortarWarning`, `KsMortarIncoming`, `KsMortarSightEye` | `Resources/Prototypes/_KS14/Entities/Objects/Weapons/Artillery/mortar.yml` |
| `KsMortarShellTrail` | `Resources/Prototypes/_KS14/Entities/Effects/SupplyPod/trail.yml` |
| Art | `Resources/Textures/_KS14/Objects/Weapons/Artillery/mortar.rsi`, `mortar_warning.rsi` |
| Sounds | `Resources/Audio/_KS14/Weapons/Mortar/` |
| Field Commander hook | `Content.Server/_KS14/FieldCommander/KsFieldCommanderRuleSystem.Mortar.cs` |
| Tests | `Content.IntegrationTests/Tests/_KS14/Mortar/` (`KsMortarWindowTest` for the window, headless; `KsMortarSightTest` for the sight, PVS on), `Tests/_KS14/FieldCommander/KsFieldCommanderMortarCrewTest.cs`, `Tests/_KS14/SupplyPod/KsSupplyPodDescentTest.cs` |

## Who fires it

- **Its crewman.** An entity with `KsMortarCrew` spawns its `mortar` where it has stood still for `setUpTime` with a
  hostile in the mortar's range, once. From then on, while it stands within `crewRange` of the mortar and moves slower
  than `maxCrewSpeed`, it fires every `interval` at the hostile between `minRange` and `maxRange` most worth a shell: one
  point for itself, one more for each other hostile within `clusterRadius`, and its `KsNpcTargetPriority` times
  `priorityWeight`. Nobody else may open a crewed mortar's window.
- **Anyone else**, once it has no crewman alive: through its window, at a compass bearing (0 is north, clockwise) and a
  distance kept to its range, every `manualCooldown`. Whoever fires it takes it over: its NPC factions become theirs. A
  crewman only down, not dead, loses the mortar for good to whoever fires it.
- **A mortar put down with no crewman** (by an admin, or on a map) is anyone's from the start, ready at once.

## The window

- **The plot** (`KsMortarAimControl`), north up: the mortar at the centre, its `minRange` and `maxRange` rings with the
  band between them shaded, N/E/S/W marks, the aim as a cross with its `spread` ring, and where its last `maxImpacts`
  shells landed, the newest strongest, with the two ranges written on their rings. The scale is not linear: the circle
  inside `minRange` gets only `InnerRadiusFraction` (a fifth) of the radius and the band takes the rest
  (`DistanceToRadius`), so the band fills the plot at any range; impacts, the aim and its spread ring (drawn to the
  band's scale) use the same mapping. A click or a drag aims at that point to a tenth of a degree and a whole tile,
  kept to the band (inside the inner circle is `minRange`); the mouse wheel moves the aim a tile in or out (5 with
  Shift).
- **The fields**, bearing and distance, are the same aim as the plot: either writes the other, and only because the
  player did something, so a state from the server never overwrites what is being typed. −/+ nudge the distance by a
  tile and the bearing by a degree, or less at long range (`BearingNudgeDegrees`: never more than a tile across at the
  distance aimed at); 5 times that with Shift. A cross of N/W/E/S buttons beside them moves the point aimed at a tile
  along the map (5 with Shift), the bearing and distance recomputed and kept to the band; both kinds are kept, as one
  moves along the bearing and the other across the map. Bearings are written to a hundredth of a degree. A field that is not a number, a bearing outside 0 to 360 or a distance outside the range turns red as
  it is typed, Fire goes off and the reason shows under the fields. Enter in either field fires.
- **Status**: rounds left, a reload bar filling over `manualCooldown`, and Ready / Reloading / Out of shells; the bearing
  readout gives three figures and a compass point (`045° NE`).
- **Fall of shot**: each shell carries `KsMortarFallOfShotComponent` (its mortar); on `SupplyPodLandedEvent` the mortar
  keeps where it landed, relative to itself, dropping the oldest past `maxImpacts`, and sends a new state.
- **The sight**, beside the plot, with `sightRadius` above 0: a live view `sightRadius` tiles around the aim, the spread
  ring drawn over it. Opening the window spawns a `sightEye` (`KsMortarSightEye`: an `Eye`, no FOV, parented to the map
  with grid traversal off) for that user and subscribes their session to it (`ViewSubscriberSystem.AddViewSubscriber`,
  as surveillance cameras do), its PVS scale cut to the sight; the state names each user's eye and the window binds a
  `ScalingViewport` to that entity's `EyeComponent.Eye` once it has arrived. The window sends its aim
  (`KsMortarAimMessage`, coalesced to one a tick); the server checks it is a number, clamps the distance, checks the
  coordinates are valid and moves the eye at most every `sightMoveInterval`, keeping the latest aim until then. The
  eye and subscription go on `BoundUIClosedEvent` (closing, the range check, the user going) and on the mortar
  component's shutdown, per user.
- The server still checks everything in `KsMortarFireMessage`: a number, clamped to the range, cooldown and rounds.

## Abandoned

Its crewman dead or gone, a mortar joins `abandonedFactions` (what its old side is hostile to, so they shoot it) and
becomes a priority target at `abandonedPriority`. With `dissolveWhenAbandoned: true` it then dissolves after `idleTime`
with nobody using it, or `emptyTime` once it has no rounds left; firing it or opening its window starts that again.
Without it (the default), it stays until destroyed. A crewed mortar never times out.

## A shot

1. A round is spent, and a shell (`shell`, a supply pod) is spawned uninitialised within `spread` of the aim point.
2. Its fall is set to `flightTime`: it lands then whatever becomes of the mortar, and its landing zone indicator
   (`KsMortarWarning`, sized to the shell's blast by `KsStrikeWarningSystem`) counts down from the shot.
3. One `incomingSound` clip is picked and timed to end as the shell lands. The shell is **seen** falling only for the
   end of its flight: `SupplyPodComponent.DescentDuration`, set to the clip's length kept between `minDescentTime` and
   `maxDescentTime` (and to the flight). Until then the client keeps the shell and its trail out of sight.
4. `travelSound` plays where it will land, `fireSound` and `farFireSound` at the mortar. Kill credit for the blast goes to
   whoever fired it (`KsKillCreditSystem.SetSender` on the shell).

## Fields (`KsMortar`)

| Field | Default | Meaning |
| --- | --- | --- |
| `shell` | required | The supply pod spawned for each shot |
| `rounds` | 20 | Shells left |
| `minRange`, `maxRange` | 30, 50 | Tiles; both crew targeting and manual fire are kept to these |
| `interval` | 12 s | How often its crewman fires |
| `manualCooldown` | 6 s | How soon anyone else may fire again |
| `clusterRadius`, `priorityWeight` | 3, 5 | Crew target scoring |
| `spread` | 1.5 | Tiles a shell may land off its aim; drawn as a ring round the aim |
| `maxImpacts` | 5 | How many of its last impacts its window plots; 0 for none |
| `sightRadius`, `sightEye`, `sightMoveInterval` | 0 (5 on `KsMortar`), `KsMortarSightEye`, 0.1 s | Its window's sight: tiles shown around the aim (0 for none), the eye spawned, how often the eye may move |
| `crewRange`, `maxCrewSpeed` | 2, 0.5 | How near and how still the crewman must be to fire |
| `flightTime` | 5 s | From the shot to the landing |
| `minDescentTime`, `maxDescentTime` | 1 s, 2 s | Bounds on how long the shell is seen falling |
| `fireSound`, `farFireSound`, `travelSound`, `incomingSound`, `loadSound` | none | Sounds of a shot; `incomingSound` is a collection, one clip a shell |
| `abandonedFactions`, `abandonedPriority` | none, 0.9 | Who an abandoned mortar belongs to, and how much it draws fire |
| `dissolveWhenAbandoned` | false | Whether an unused, uncrewed mortar dissolves |
| `idleTime`, `emptyTime` | 3 min, 10 s | How long that takes, with rounds and without |

`KsMortarCrew`: `mortar` (required), `setUpTime` (2 s).

## Field Commander

The mode does not run the mortar; it hands one to an NPC crewman and reacts to two events raised on the crewman, in
`KsFieldCommanderRuleSystem.Mortar.cs`:

- `KsMortarDeployedEvent`: the unit stops taking orders and holds by the mortar.
- `KsMortarLostEvent` (the mortar destroyed, or taken over by someone firing it while the crewman was down): the unit
  is sent at the defenders again, its stand-off forgotten.

Mortars bought by the commander opt in to `dissolveWhenAbandoned`, so one left behind clears itself up; a standalone
`KsMortar` does not.
