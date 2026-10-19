#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Atmos.FoamBlanket;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._KS14.Atmos.ChemicalFire;
using Content.Shared._KS14.Atmos.FoamBlanket;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Atmos;

/// <summary>
///     A chemfire that fights foam (thermite's like) is not put out the moment foam lands on it: it boils the blanket
///         over it away with its heat, so a blanket left alone loses, and one kept topped up wins.
/// </summary>
[TestOf(typeof(KsFoamBlanketSystem))]
public sealed class KsFoamContestTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: BaseChemicalFire
  id: KsTestFoamContestFire
  components:
  - type: ChemicalFire
    color: '#ffc291'
    duration: 120
    temperature: 2900
    heatPower: 12000
    extinguishable: false
    foamSmotherResistance: 6
    foamHeatFraction: 0.01
";

    // Deliberately NOT static: it only exists in the test YAML above, and the YAML linter validates static ids.
    private readonly EntProtoId _fightingFirePrototype = "KsTestFoamContestFire";

    private static readonly EntProtoId BlanketPrototype = "KsFoamBlanket";
    private static readonly EntProtoId ThermiteFirePrototype = "ChemicalFireThermite";

    private const int TicksPerSecond = 30;

    private async Task<TestMapData> CreateBreathableTestMap()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var mixture = new GasMixture(Atmospherics.CellVolume) { Temperature = Atmospherics.T20C };
            mixture.SetMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
            mixture.SetMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);

            server.EntMan.System<AtmosphereSystem>().SetMapAtmosphere(testMap.MapUid, space: false, mixture);
            server.EntMan.EnsureComponent<GridAtmosphereComponent>(testMap.Grid.Owner);
        });

        await server.WaitRunTicks(30);
        return testMap;
    }

    /// <summary>
    ///     Lays foam on the tile until the blanket is full.
    /// </summary>
    private void FillBlanket(TestMapData testMap)
    {
        var blanketSystem = Pair.Server.EntMan.System<KsFoamBlanketSystem>();
        for (var i = 0; i < 30; i++)
            blanketSystem.Deploy(BlanketPrototype, testMap.Tile, 1f, playSound: false);
    }

    [Test]
    public async Task ThermiteFightsFoam()
    {
        await Pair.Server.WaitAssertion(() =>
        {
            Assert.That(SProtoMan.Index(ThermiteFirePrototype).TryComp<ChemicalFireComponent>(out var fireComponent, Pair.Server.EntMan.ComponentFactory));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(fireComponent!.FoamSmotherable, "Foam cannot smother thermite at all.");
                Assert.That(fireComponent.FoamSmotherResistance, Is.GreaterThan(0f), "Foam smothers thermite at once.");
                Assert.That(fireComponent.FoamHeatFraction, Is.GreaterThan(0f), "Thermite does not boil the foam over it.");
            }
        });
    }

    /// <summary>
    ///     One full blanket that nobody tops up is boiled away by the fire before it can smother it, and the fire burns
    ///         on. On the way, the fire is visibly losing ground, and the blanket goes faster than it would on its own.
    /// </summary>
    [Test]
    public async Task BlanketLeftAloneLoses()
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;
        var testMap = await CreateBreathableTestMap();
        var blanketSystem = entityManager.System<KsFoamBlanketSystem>();
        Entity<ChemicalFireComponent> fire = default;

        await server.WaitAssertion(() =>
        {
            fire = entityManager.System<SharedChemicalFireSystem>().SpawnChemicalFire(_fightingFirePrototype, testMap.Tile)!.Value;
            FillBlanket(testMap);
            Assert.That(entityManager.EntityExists(fire.Owner), "The foam put the fire out at once.");
        });

        await server.WaitRunTicks(TicksPerSecond * 2);

        var progressSeen = 0f;
        var foamAfterTwoSeconds = 0f;
        await server.WaitAssertion(() =>
        {
            progressSeen = entityManager.GetComponent<ChemicalFireComponent>(fire.Owner).SmotherProgress;
            Assert.That(blanketSystem.TryGetBlanket(testMap.Grid.Owner, testMap.Tile.GridIndices, out var blanket), "The blanket went too soon.");
            foamAfterTwoSeconds = blanket.Comp.Foam;
        });

        await server.WaitRunTicks(TicksPerSecond * 10);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(progressSeen, Is.GreaterThan(0f), "The fire showed no sign of losing to the foam.");
                // Left alone, a blanket loses 0.01u a second, and boils off no more than 0.03u over hot air.
                Assert.That(foamAfterTwoSeconds, Is.LessThan(1.5f - 2f * 0.04f), "The fire did not boil the foam away.");
                Assert.That(blanketSystem.TryGetBlanket(testMap.Grid.Owner, testMap.Tile.GridIndices, out _), Is.False,
                    "The fire did not boil the whole blanket away.");
                Assert.That(entityManager.EntityExists(fire.Owner) && !entityManager.IsQueuedForDeletion(fire.Owner),
                    "One blanket left alone smothered the fire.");
            }
        });

        // With the foam gone, the fire gets back up.
        await server.WaitRunTicks(TicksPerSecond * 4);
        await server.WaitAssertion(() =>
            Assert.That(entityManager.GetComponent<ChemicalFireComponent>(fire.Owner).SmotherProgress, Is.Zero, "The fire did not recover once the foam was gone."));
    }

    /// <summary>
    ///     A blanket kept topped up, as spraying does, smothers the fire within a few seconds, and nothing that foam can
    ///         smother relights under it.
    /// </summary>
    [Test]
    public async Task BlanketKeptUpWins()
    {
        var server = Pair.Server;
        var entityManager = server.EntMan;
        var testMap = await CreateBreathableTestMap();
        var chemicalFireSystem = entityManager.System<SharedChemicalFireSystem>();
        Entity<ChemicalFireComponent> fire = default;

        await server.WaitAssertion(() =>
        {
            fire = chemicalFireSystem.SpawnChemicalFire(_fightingFirePrototype, testMap.Tile)!.Value;
        });

        // Long enough for the client to have the fire, but not for it to burn out.
        await Pair.RunTicksSync(10);
        var clientFireUid = ToClientUid(fire.Owner);

        var clientProgressSeen = 0f;
        var outAfter = -1;
        for (var tick = 0; tick < TicksPerSecond * 8; tick += 5)
        {
            await server.WaitPost(() => FillBlanket(testMap));
            await Pair.RunTicksSync(5);

            if (!entityManager.EntityExists(fire.Owner) || entityManager.IsQueuedForDeletion(fire.Owner))
            {
                outAfter = tick + 5;
                break;
            }

            await Pair.Client.WaitPost(() =>
            {
                if (Pair.Client.EntMan.TryGetComponent<ChemicalFireComponent>(clientFireUid, out var clientFireComponent))
                    clientProgressSeen = MathF.Max(clientProgressSeen, clientFireComponent.SmotherProgress);
            });
        }

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(outAfter, Is.InRange(TicksPerSecond * 1, TicksPerSecond * 6),
                    $"The fire went out after {outAfter} ticks, not within a few seconds of steady foam.");
                Assert.That(clientProgressSeen, Is.GreaterThan(0f), "The client never saw the fire losing to the foam.");
                Assert.That(chemicalFireSystem.SpawnChemicalFire(_fightingFirePrototype, testMap.Tile), Is.Null,
                    "The fire relit under the foam that put it out.");
            }
        });
    }
}
