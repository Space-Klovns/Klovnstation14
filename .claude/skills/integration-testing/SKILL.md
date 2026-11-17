---
name: integration-testing
description: How to write and run integration tests in this repo - the GameTest fixture and pool settings, driving server and client, players, eyes, input movers, console and VV commands, cvars, what a headless client can and cannot check, how to tell a real failure from pool noise, proving a test fails, PVS tests, mechanism versus content tests, and tests that don't break on the next balance pass. Use when writing or debugging any test under Content.IntegrationTests, deciding what data a test should use, or deciding how to prove a change works.
---

# Integration testing

CONTRIBUTING.md §5 (build Release, test Debug) and §6 (the testing rules, `SandboxTest`, `CLIENT` cvars and
disconnected clients, the "Pool manager has not been initialized" shape) state the rules. This skill has what's behind
them, and the mechanics. NPC-specific testing is in the `npc-htn` skill.

## The fixture

Fork tests live in `Content.IntegrationTests/Tests/_KS14/<Feature>/` and derive from `GameTest`
(`Content.IntegrationTests/Fixtures/`):

```csharp
[TestFixture]
[TestOf(typeof(KsSomethingSystem))]
public sealed class KsSomethingTest : GameTest
{
    // Arrow syntax only: the pool reads this more than once and expects a fresh instance each time.
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestSomething
  components:
  - type: PointLight
";

    [Test]
    public async Task SomethingHappens()
    {
        var server = Pair.Server;
        // ...
    }
}
```

| Setting | Use it when |
| --- | --- |
| `Connected = true` (the default) | Anything involving the client. A disconnected client never starts its entity systems, so resolving one throws. |
| `Dirty = true` | The test leaves state behind, such as a changed cvar, an attached player or spawned maps. The pair is cleaned before reuse. |
| `Fresh = true` | It needs a brand-new pair. That's slow, so only when reuse genuinely breaks it. |
| `Destructive = true` | It ruins the pair. It is thrown away afterwards. |
| `InLobby`, `DummyTicker` | Round-flow tests. |

`GameTest` also gives you helpers: `SSpawn`/`CSpawn`, `SComp`/`CComp`, `SEntity<T>`, `ToClientUid`/`ToServerUid`,
`SDeleteNow`, and `OverrideCVar(side, cvar, value)`, which syncs and is undone after the test. Read
`GameTest.Entities.cs` and `GameTest.CVars.cs` before writing your own.

## Driving the pair

- **Mutate on the game thread.** Use `await server.WaitPost(() => ...)`, and `server.WaitAssertion(() => ...)`
  for asserts. Never touch entities from the test thread directly.
- **Advance time** with `await Pair.RunTicksSync(n)`, which ticks both sides and lets state reach the client. A
  client assertion straight after a server change, with no ticks between, reads stale state.
- **A test map:** `var testMap = await Pair.CreateTestMap();`. Spawn on `testMap.MapId`, or on `testMap.Grid` for
  anything that needs floor.
- **Read client state** with `client.WaitPost`, and use `AllEntityQueryEnumerator` unless you mean to skip paused
  entities (CONTRIBUTING §4). Client-only entities, such as stand-in lights or effects, exist only there.

### Putting the player, and their eye, somewhere

Systems that only run near the viewer need the player attached to something at the right spot:

```csharp
await server.WaitPost(() =>
{
    var viewerUid = server.EntMan.SpawnEntity(null, new MapCoordinates(position, mapId));
    server.EntMan.EnsureComponent<EyeComponent>(viewerUid);
    server.PlayerMan.SetAttachedEntity(Pair.Player!, viewerUid);
});
```

Add a control where the viewer is far away and the feature must stay idle. Otherwise "only runs nearby" is
untested (`KsPortalLightTransferTest.LightIsNotCarriedForAViewerFarFromBothEnds`).

### Moving like a player

Setting a velocity skips the mover, along with its friction, prediction-shared code and collision response. To
test what a walking player does, hold a direction on an `InputMover`:

```csharp
var mover = new Entity<InputMoverComponent>(mobUid, server.EntMan.GetComponent<InputMoverComponent>(mobUid));
server.EntMan.System<SharedMoverController>().SetVelocityDirection(mover, Direction.North, subTick: 0, enabled: true);
```

The mob prototype needs a `KinematicController` body. A `Dynamic` one passes in Release and trips a debug assert
in Debug (CONTRIBUTING §5). `KsPortalTravelTest.TestWalkingMoverLoopsAHallway` is a worked example. It samples the
position every few ticks and counts the jumps, rather than asserting only on the end state.

### Commands, and VV

Run a command exactly as an admin would. That tests its parsing and permissions as well as the system:

```csharp
var consoleHost = server.ResolveDependency<IConsoleHost>();
await server.WaitPost(() => consoleHost.ExecuteCommand($"ksportallink {server.EntMan.GetNetEntity(a)} {server.EntMan.GetNetEntity(b)}"));
await server.WaitPost(() => consoleHost.ExecuteCommand($"vvwrite /entity/{uid.Id}/KsPortalView/Width 20"));
```

Commands take `NetEntity` or local-uid numbers according to their own parsing, so check which. `vvwrite` paths take the
local uid number, as in CONTRIBUTING §4 ("ViewVariables").

### CVars

- `await OverrideCVar(Side.Server, KsCCVars.Something, true)` for a `SERVER | REPLICATED` cvar. It reaches the
  client after a tick.
- A `CLIENT` cvar can only be set with `Side.Client`. Setting it on the server is silently ignored (CONTRIBUTING §6).
- Toggle the feature within the test and assert both ways. "Nothing happens while it's off" is half of the proof.

### Mechanics that cost a lookup

- **Usings.** `GameTest` is in `Content.IntegrationTests.Fixtures`, `Pair` helpers in `Content.IntegrationTests.Pair`,
  plus `NUnit.Framework` and `System.Threading.Tasks`. Forgetting them gives a bare `GameTest could not be found`.
- **Ticks from seconds.** `(int)(seconds * (float)server.ResolveDependency<IGameTiming>().TickRate)`, passed to
  `Pair.RunTicksSync`. Don't hardcode a tick rate.
- **Client entity from a server one.** `Pair.ToClientUid(serverUid)`, then read the client component in
  `client.WaitAssertion`. Run ticks first so the state has arrived.
- **Sprite layer state on the client.** `spriteComponent[spriteSystem.LayerMapGet((uid, spriteComponent), key)]` gives a
  layer with `.Scale`, `.Rotation` and so on. Assert a mid-animation value inside a range, not an exact number: the
  headless client's `FrameUpdate` runs, but not on your tick boundaries.
- **Spawn a real prototype** (a `SupplyPodCargoBase`, say) with `SpawnEntity(proto, new EntityCoordinates(testMap.Grid,
  0.5f, 0.5f))` instead of writing a `[TestPrototypes]` copy, when the real wiring is what you're testing.
- **Proving red.** Comment out the one client or server line that produces the behaviour and rerun that test alone.
  Restore it from a copy, not by retyping.

## What a headless client can check

The test client's graphics are `ClydeHeadless`. Nothing is rendered, overlays never `Draw`, and controls never
`Draw`, but `FrameUpdate` runs and UI layout and measuring work with real font metrics. So test rendering work
through the state it leaves behind and through pure functions. See the `rendering` skill's Testing section, and
report anything visual as unverified until it has been seen in game.

## Running

```sh
# one feature, Debug, with a readable failure list
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c Debug --filter "FullyQualifiedName~_KS14.Portal" \
  --logger "trx;LogFileName=portal.trx"

# a single test, when proving it red or when it only fails in a batch
dotnet test ... -c Debug --filter "FullyQualifiedName~KsPortalTravelTest.TestWalkingMoverLoopsAHallway"
```

- **A running client or server locks `bin/`.** Build into another folder two levels down, so the content root is
  still found at `../../`: `-p:OutputPath="<repo>/bin/kstest/"`. Delete it afterwards.
- **"NotExecuted: Test was dirty-disposed"** in a batch can hide a real assertion failure. Rerun that test alone
  (the `npc-htn` skill has the details).
- **Parametrise** with `[TestCase(2.5f)]`, `[TestCase(6f)]` when a geometric rule could hold at one size and fail at
  another. The hallway test runs at two lengths for this reason.

## A test that never fails is worse than no test

Two bugs in one feature were each "covered" by a test that passed with the bug present, and such a test is the reason
nobody looks again. Two ways to get there without carelessness:

- **Something other than the code under test satisfies the assertion.** A test named for landing on a crossing platform
  passed with the obstruction event unsubscribed, because the ordinary slice-crossing path put the faller on that map
  anyway. When a subsystem has two routes to the same outcome, name the one the test pins and test the other separately.
- **The environment is more permissive than production**, as with PVS below.

Fix the test in the same change as the bug; deleting it beats leaving it. The reverse happens too: in a batch, a failed
`GameTest` can show as `Skipped` or `NotExecuted` with only `Test was dirty-disposed.` in the trx and console. Treat that
as a failure and rerun it alone with `--logger "console;verbosity=detailed"`.

### Proving it fails, in practice

The cheapest way to break the code and watch the test go red:

1. Change one line of the fix, such as returning early or swapping the sign, not the whole feature.
2. Run just that test in Debug and see it fail **with the message you wrote**. A failure somewhere else means the
   test is broken, not proven.
3. Put the line back, rerun, and see it pass.

If it can't be made to fail, it is checking something other than your code. Find the second route to the same
outcome and pin the one you mean.

## PVS tests are vacuous by default

When something is on the server, in the right place, with the right components, and the client can't see it, it was far
more often never *sent* than drawn wrong. Two traps make a PVS test pass with the bug live:

1. **Pooled pairs run with `net.pvs` off**, sending every entity to every client. Turn it on:
   `await OverrideCVar(Side.Server, CVars.NetPVS, true);`
2. **Leaving PVS detaches an entity on the client rather than deleting it** (`MetaDataFlags.Detached`), so
   `TryGetEntity` stays `true` for anything the client was ever told about. Assert on an entity spawned **after** the
   state under test began, or check the detached flag.

Include a control entity that must *not* arrive: without one, "everything arrived" and "PVS isn't filtering" are the
same green test.

## Mechanism tests and content tests

YAML is where balance and gameplay get tweaked, often, by people who shouldn't have to think about tests. A test either
proves a *mechanism* works or that *content* is right; decide which before picking a prototype.

| | Mechanism test | Content test |
| --- | --- | --- |
| Proves | The system does what it says, whatever it is given | This real prototype, map or chain is set up as designed |
| Data | `[TestPrototypes]`, with whatever numbers suit the test | Live prototypes, every number derived from them |
| Example | "A catalysed reaction runs only in a reactor holding its catalyst" (`KsCatalyticReactorTests`) | "Sulfur must be removed before reforming" (`KsRefineryReactionTests`) |

- **Default to a mechanism test.** Spawning a real machine to exercise it is fine, as the machine is the subject; its
  feed, catalyst and temperatures should be the test's own.
- **Rely on what already enforces a behaviour.** Find what fails today if it breaks: a behaviour needs one test that
  catches it, not one per feature using it. A feature built on a tested system tests only what it adds ("my reagent
  also boils" is the boiling test's job).
- **Generalise rather than add a case.** A test named for a feature that really exercises something generic loses the
  feature's name and data. A hardcoded list of ids is the usual sign: walk whatever defines the kind instead.
  `KsRpdPipeLayerTests` walks the RPD's own settings and so covers every layered device, upstream's included.
- **Assert what a thing does, not how its prototype is written.** "This entity has this component" or "this field is
  this value" fails only when someone changes it on purpose. Check the behaviour (a mob stopped by the machine), or
  leave it to the YAML linter and `UninitializedSaveTest`. The exception is a partial prototype: test that the merged
  prototype has both your change and upstream's data (`KsLobbyMusicPartialTest`), since a partial that stopped applying
  looks exactly like the upstream prototype.
- **Content tests find things by what they are**, not by id, coordinates or count, and assert the design rather than a
  yield. `KsRefineryGridTests` takes the tank holding the most bitumen as the residue.

## Tests bound to today's tuning

A test that copies a prototype's numbers (a loop count, a wait, a threshold) checks those numbers, not the mechanic: it
fails on the next retune with no bug, or keeps passing while no longer exercising what it's named for. One foam-blanket
balance pass (`maxFoam` 1.5 → 3.5, `latentHeat` 12000 → 45000) and a shorter shot lifetime broke six tests: a fixed 30
deployments no longer filled the blanket, a 4 s wait no longer boiled it off (it took 17), and "boils at least half its
`PuddleBoilRate`" assumed the rate was the limit when the puddle's spare heat ran out first. Derive what the test depends
on from the prototype it spawns, with the mechanism as the bound:

```csharp
// fills the blanket whatever it holds
for (var i = 0; i < 1000 && blanketSystem.Deploy(BlanketPrototype, tileRef, 1f) > 0f; i++)
{
}

// waits as long as the fire's heat takes, from the prototypes' own values
ProtoMan.Index(BlanketPrototype).TryGetComponent<KsFoamBlanketComponent>(out var blanketPrototype, Factory);
var boilSeconds = blanketPrototype.MaxFoam * energyPerUnit / (firePrototype.HeatPower * firePrototype.FoamHeatFraction);
await server.WaitRunTicks((int)MathF.Ceiling(boilSeconds * 1.5f + 1f) * TicksPerSecond);
```

- **Bound by every limit the code applies**: if it takes the minimum of a rate and an energy budget, so does the
  expected value.
- **Tolerances come from the mechanism too**: a value that changes every update and reaches the client a state later is
  compared within one update's change (`DecayRate * UpdateInterval`), not `0.0001f`.
- **Assert the design limit separately** where it matters ("boiling a full blanket off takes under 30 s"), so a tuning
  that breaks the *gameplay* still fails, saying so.
- **Waiting for something to happen**: loop until it has, up to a generous cap, rather than wait a copied duration.
- A hard-coded number is fine when it's the test's own setup (a `[TestPrototypes]` entity, a spilled volume).

When a test fails after a balance change, first check whether the change broke the test's own arithmetic, then update
it to derive the value, not to match the new constant.
