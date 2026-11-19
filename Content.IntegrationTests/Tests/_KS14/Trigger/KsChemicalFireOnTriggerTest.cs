#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Trigger.Systems;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._KS14.Atmos.ChemicalFire;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Trigger.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Trigger;

/// <summary>
///     A round with <c>KsChemicalFireOnTrigger</c> (the flamer tank's) leaves a chemical fire where it lands, out to its
///         radius and no further, and only when it goes off.
/// </summary>
[TestFixture]
[TestOf(typeof(KsChemicalFireOnTriggerSystem))]
public sealed class KsChemicalFireOnTriggerTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    // The chemical fire the round lights, as a flamer's would be: what is under test is the round.
    private static readonly EntProtoId ChemicalFire = "ChemicalFire";

    // Ids that exist only in the test prototypes below.
    private readonly EntProtoId _round = "KsTestChemicalFireRound";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestChemicalFireRound
  components:
  - type: KsChemicalFireOnTrigger
    fire: ChemicalFire
    radius: 1
";

    [Test]
    public async Task TheRoundLightsWhereItLands()
    {
        var server = Pair.Server;
        var entMan = server.EntMan;
        var testMap = await Pair.CreateTestMap();
        EntityUid roundUid = default;

        await server.WaitPost(() =>
        {
            var mixture = new GasMixture(Atmospherics.CellVolume) { Temperature = Atmospherics.T20C };
            mixture.SetMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
            mixture.SetMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);
            entMan.System<AtmosphereSystem>().SetMapAtmosphere(testMap.MapUid, space: false, mixture);
            entMan.EnsureComponent<GridAtmosphereComponent>(testMap.Grid.Owner);

            var mapSystem = entMan.System<SharedMapSystem>();
            for (var x = -5; x <= 5; x++)
            {
                for (var y = -5; y <= 5; y++)
                    mapSystem.SetTile(testMap.Grid.Owner, testMap.Grid.Comp, new Vector2i(x, y), testMap.Tile.Tile);
            }

            roundUid = entMan.SpawnEntity(_round, new EntityCoordinates(testMap.Grid.Owner, 0.5f, 0.5f));
        });

        // For the air to settle on the new floor: a chemical fire needs some to burn in.
        await Pair.RunTicksSync(60);

        var chemicalFireSystem = entMan.System<SharedChemicalFireSystem>();
        await server.WaitAssertion(() =>
        {
            Assert.That(chemicalFireSystem.TryGetChemicalFire(testMap.Grid.Owner, Vector2i.Zero, ChemicalFire.Id, out _), Is.False,
                "nothing should burn before the round goes off");

            entMan.System<TriggerSystem>().Trigger(roundUid, key: TriggerSystem.DefaultTriggerKey);
        });

        await Pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            foreach (var tile in new[] { Vector2i.Zero, new Vector2i(1, 0), new Vector2i(0, -1) })
            {
                Assert.That(chemicalFireSystem.TryGetChemicalFire(testMap.Grid.Owner, tile, ChemicalFire.Id, out _), Is.True,
                    $"the round should leave a chemical fire on {tile}");
            }

            Assert.That(chemicalFireSystem.TryGetChemicalFire(testMap.Grid.Owner, new Vector2i(3, 0), ChemicalFire.Id, out _), Is.False,
                "and none past its radius");
        });
    }
}
