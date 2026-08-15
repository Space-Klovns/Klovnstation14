using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Sensors;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Sensors;

public sealed class KsSensorSystemTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestVisualSensor
  name: test optical array
  components:
  - type: KsSensor
    maxRange: 200
    providesName: true
    requireExternalMount: false
    intel:
    - KsIntelSize
    - KsIntelMass
    - KsIntelTopSpeed
  - type: KsVisualSearch

- type: entity
  id: KsTestDatalinkTransmitter
  name: test datalink transmitter
  components:
  - type: KsDatalinkTransmitter
    maxRange: 1000

- type: entity
  id: KsTestDatalinkReceiver
  name: test datalink receiver
  components:
  - type: KsDatalinkReceiver

- type: entity
  id: KsTestDatalinkReceiverOfftune
  name: mistuned test datalink receiver
  components:
  - type: KsDatalinkReceiver
    frequency: 2600
";

    /// <summary>
    ///     Sensor grid A (sensor + transmitter) sees target grid B nearby;
    ///         receiver grid C is beyond sensor range but inside transmitter
    ///         range, so it must learn about B (relayed) and A (self-report)
    ///         purely over the datalink.
    /// </summary>
    [Test]
    public async Task TestVisualSearchAndDatalink()
    {
        var pair = Pair;
        var server = pair.Server;

        var map = await pair.CreateTestMap();
        var entManager = server.ResolveDependency<IEntityManager>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        var xformSystem = entManager.System<SharedTransformSystem>();

        var gridA = default(Entity<Robust.Shared.Map.Components.MapGridComponent>);
        var gridB = default(Entity<Robust.Shared.Map.Components.MapGridComponent>);
        var gridC = default(Entity<Robust.Shared.Map.Components.MapGridComponent>);
        var gridD = default(Entity<Robust.Shared.Map.Components.MapGridComponent>);

        await server.WaitPost(() =>
        {
            entManager.DeleteEntity(map.Grid);

            gridA = mapManager.CreateGridEntity(map.MapId);
            gridB = mapManager.CreateGridEntity(map.MapId);
            gridC = mapManager.CreateGridEntity(map.MapId);
            gridD = mapManager.CreateGridEntity(map.MapId);

            // Big enough that grid B clears the <10 mass junk filter.
            var tiles = new List<(Vector2i Index, Tile Tile)>();
            for (var x = 0; x < 8; x++)
            {
                for (var y = 0; y < 8; y++)
                {
                    tiles.Add((new Vector2i(x, y), new Tile(1)));
                }
            }

            mapSystem.SetTiles(gridA.Owner, gridA.Comp, tiles);
            mapSystem.SetTiles(gridB.Owner, gridB.Comp, tiles);
            mapSystem.SetTiles(gridC.Owner, gridC.Comp, tiles);
            mapSystem.SetTiles(gridD.Owner, gridD.Comp, tiles);

            // B within sensor range (200) of A; C and D outside sensor range
            // but within transmitter range (1000).
            xformSystem.SetLocalPosition(gridB.Owner, new Vector2(50f, 0f));
            xformSystem.SetLocalPosition(gridC.Owner, new Vector2(500f, 0f));
            xformSystem.SetLocalPosition(gridD.Owner, new Vector2(500f, 200f));

            entManager.SpawnEntity("KsTestVisualSensor", new EntityCoordinates(gridA.Owner, new Vector2(0.5f, 0.5f)));
            entManager.SpawnEntity("KsTestDatalinkTransmitter", new EntityCoordinates(gridA.Owner, new Vector2(1.5f, 0.5f)));
            entManager.SpawnEntity("KsTestDatalinkReceiver", new EntityCoordinates(gridC.Owner, new Vector2(0.5f, 0.5f)));
            // D listens on the wrong frequency: it must hear nothing.
            entManager.SpawnEntity("KsTestDatalinkReceiverOfftune", new EntityCoordinates(gridD.Owner, new Vector2(0.5f, 0.5f)));
        });

        // Comfortably more than one 0.5s sensor tick.
        await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entManager.TryGetComponent<KsSensorContactPoolComponent>(gridA.Owner, out var poolA),
                    "sensor grid A never built a contact pool");
                Assert.That(poolA!.Contacts.ContainsKey(gridB.Owner), "A's visual search did not detect B");
                Assert.That(poolA.Contacts.ContainsKey(gridC.Owner), Is.False, "A detected C beyond sensor range");

                // C's picture arrived purely over datalink.
                Assert.That(entManager.TryGetComponent<KsSensorContactPoolComponent>(gridC.Owner, out var poolC),
                    "receiver grid C never built a contact pool");
                Assert.That(poolC!.Contacts.ContainsKey(gridB.Owner), "B was not relayed to C over datalink");
                Assert.That(poolC.Contacts.ContainsKey(gridA.Owner), "A's datalink self-report never reached C");
                Assert.That(poolC.Contacts.ContainsKey(gridC.Owner), Is.False, "C was told about itself");

                var bViaC = poolC.Contacts[gridB.Owner];
                Assert.That(bViaC.Sources.Values.Min(s => s.Hops), Is.EqualTo(1), "relayed detection of B should be one hop");

                var bViaA = poolA.Contacts[gridB.Owner];
                Assert.That(bViaA.Sources.Values.Min(s => s.Hops), Is.EqualTo(0), "own detection should be zero hops");

                Assert.That(bViaC.Sources.Values.Any(s => s.Name != null), "identified name was lost over the datalink");

                // Everything a sensor shows travels the datalink, not just position.
                Assert.That(bViaC.Sources.Values.Any(s => s.Intel is { Count: > 0 }),
                    "relayed detection of B lost its size/mass/top-speed intel over the datalink");

                // A transmitter's self-report carries the same intel about its own
                // grid, so a receiver-only grid learns the transmitting ship's stats,
                // not merely where it is.
                var aViaC = poolC.Contacts[gridA.Owner];
                var aNet = entManager.GetNetEntity(gridA.Owner);
                var aSelfReport = aViaC.Sources.Values.First(s => s.SourceGridNet == aNet);
                Assert.That(aSelfReport.Intel, Is.Not.Null, "A's self-report reached C carrying no intel at all");
                Assert.That(aSelfReport.Intel!.Keys.Select(k => k.Id),
                    Is.SupersetOf(new[] { "KsIntelSize", "KsIntelMass", "KsIntelTopSpeed" }),
                    "A's self-report is missing size/mass/top-speed intel");
                // Every readout must carry a real evaluated value, not a blank: A has
                // no thrusters, so its top speed reads the engine-less string rather
                // than an empty line.
                Assert.That(aSelfReport.Intel!.Values, Has.All.Not.Empty, "A's self-report has a blank intel value");

                // Frequency discipline: a mistuned receiver hears nothing. D also sits
                // beyond A's sensor range, so only datalink could populate its pool.
                if (entManager.TryGetComponent<KsSensorContactPoolComponent>(gridD.Owner, out var poolD))
                    Assert.That(poolD.Contacts, Is.Empty, "mistuned receiver D ingested a broadcast");
            });
        });
    }
}
