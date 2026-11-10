---
name: integration-testing
description: How to write and run integration tests in this repo - the GameTest fixture and pool settings, driving server and client, players, eyes, input movers, console and VV commands, cvars, what a headless client can and cannot check, and how to tell a real failure from pool noise. Use when writing or debugging any test under Content.IntegrationTests, or deciding how to prove a change works.
---

# Integration testing

The rules that cost the most are already in CONTRIBUTING.md, so read those first. They are §5 (build Release, test
Debug) and §6: prove each test fails, `SandboxTest`, PVS being off in pooled pairs, `CLIENT` cvars and disconnected
clients, and the "Pool manager has not been initialized" shape. NPC-specific testing is in the `npc-htn` skill.
This skill covers the mechanics around them.

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

## Proving it fails, in practice

CONTRIBUTING §6 says to break the code and watch the test go red. The cheapest way to do that:

1. Change one line of the fix, such as returning early or swapping the sign, not the whole feature.
2. Run just that test in Debug and see it fail **with the message you wrote**. A failure somewhere else means the
   test is broken, not proven.
3. Put the line back, rerun, and see it pass.

If it can't be made to fail, it is checking something other than your code. Find the second route to the same
outcome and pin the one you mean.
