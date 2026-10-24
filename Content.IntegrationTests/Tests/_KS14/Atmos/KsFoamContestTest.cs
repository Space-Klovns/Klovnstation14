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
# Thermite's own fire, burning long enough to watch the contest play out.
- type: entity
  parent: ChemicalFireThermite
  id: KsTestFoamContestFire
  components:
  - type: ChemicalFire
    duration: 120
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
        for (var i = 0; i < 1000 && blanketSystem.Deploy(BlanketPrototype, testMap.Tile, 1f, playSound: false) > 0f; i++)
        {
        }
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

            // The design, from the prototypes: a full blanket held full wins within a few seconds, and one left alone
            //      boils off before the progress it adds (maxFoam^2 / (2 * boil rate * resistance)) reaches 1.
            Assert.That(SProtoMan.Index(BlanketPrototype).TryComp<KsFoamBlanketComponent>(out var blanketComponent, Pair.Server.EntMan.ComponentFactory));
            var energyPerUnit = blanketComponent!.SpecificHeat * (blanketComponent.BoilingPoint - blanketComponent.FoamTemperature) + blanketComponent.LatentHeat;
            var boilRate = fireComponent.HeatPower * fireComponent.FoamHeatFraction / energyPerUnit;
            using (Assert.EnterMultipleScope())
            {
                Assert.That(fireComponent.FoamSmotherResistance / blanketComponent.MaxFoam, Is.LessThan(6f),
                    "Steady spraying takes too long to smother thermite.");
                Assert.That(blanketComponent.MaxFoam * blanketComponent.MaxFoam / (2f * boilRate * fireComponent.FoamSmotherResistance), Is.LessThan(1f),
                    "One full blanket left alone smothers thermite before it boils away.");
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
        var foamLostAlone = 0f;
        var maxFoam = 0f;
        await server.WaitAssertion(() =>
        {
            progressSeen = entityManager.GetComponent<ChemicalFireComponent>(fire.Owner).SmotherProgress;
            Assert.That(blanketSystem.TryGetBlanket(testMap.Grid.Owner, testMap.Tile.GridIndices, out var blanket), "The blanket went too soon.");
            foamAfterTwoSeconds = blanket.Comp.Foam;
            maxFoam = blanket.Comp.MaxFoam;
            // Left alone in room air, a blanket only drains. Not its boil rate over hot air: that is a separate
            //      floor, which a retuning can put above what this fire boils off in its first seconds.
            foamLostAlone = 2f * blanket.Comp.DecayRate;
        });

        await server.WaitRunTicks(TicksPerSecond * 10);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(progressSeen, Is.GreaterThan(0f), "The fire showed no sign of losing to the foam.");
                Assert.That(foamAfterTwoSeconds, Is.LessThan(maxFoam - foamLostAlone), "The fire did not boil the foam away.");
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
