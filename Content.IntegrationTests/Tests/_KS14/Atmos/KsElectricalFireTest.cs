#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.ArcFlash;
using Content.Server._KS14.Atmos.FoamBlanket;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Fluids.EntitySystems;
using Content.Shared._KS14.Atmos.ChemicalFire;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Construction;
using Content.Shared.FixedPoint;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Atmos;

/// <summary>
///     Electrical fires: an arc flash starts them on what threw it and on the machines its bolts hit, never inside a
///         wall, and only carbon dioxide puts them out.
/// </summary>
[TestOf(typeof(ArcFlashSystem))]
public sealed class KsElectricalFireTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: KsTestElectricalFireEmitter
  components:
  - type: ArcFlashDeconstructable
    lightningRange: 4
    lightningAmount: 3

- type: entity
  id: KsTestElectricalFireMachine
  components:
  - type: LightningTarget
    priority: 1
    lightningExplode: false
  - type: ApcPowerReceiver

- type: entity
  id: KsTestElectricalFireBystander
  components:
  - type: LightningTarget
    lightningExplode: false
";

    // Deliberately NOT static: these only exist in the test YAML above, and the YAML linter validates static ids.
    private readonly EntProtoId _emitterPrototype = "KsTestElectricalFireEmitter";
    private readonly EntProtoId _machinePrototype = "KsTestElectricalFireMachine";
    private readonly EntProtoId _bystanderPrototype = "KsTestElectricalFireBystander";
    // From KsFirefightingTest's test YAML.
    private readonly EntProtoId _flammablePrototype = "KsTestFirefightingFlammable";

    private static readonly EntProtoId ElectricalFirePrototype = "ChemicalFireElectrical";
    private static readonly EntProtoId ChemicalFirePrototype = "ChemicalFire";
    private static readonly EntProtoId BlanketPrototype = "KsFoamBlanket";
    private static readonly EntProtoId WallPrototype = "WallSolid";
    private static readonly ProtoId<ReagentPrototype> WaterReagent = "Water";
    private static readonly ProtoId<ReagentPrototype> CarbonDioxideReagent = "CarbonDioxide";

    private const int TicksPerSecond = 30;

    private static readonly Vector2i Centre = new(0, 0);
    private static readonly Vector2i East = new(1, 0);
    private static readonly Vector2i West = new(-1, 0);

    /// <summary>
    ///     A 3x3 floor of standard air.
    /// </summary>
    private async Task<TestMapData> CreateRoom()
    {
        var server = Pair.Server;
        var testMap = await Pair.CreateTestMap();

        await server.WaitPost(() =>
        {
            var mapSystem = server.EntMan.System<SharedMapSystem>();
            for (var x = -1; x <= 1; x++)
            {
                for (var y = -1; y <= 1; y++)
                    mapSystem.SetTile(testMap.Grid.Owner, testMap.Grid.Comp, new Vector2i(x, y), testMap.Tile.Tile);
            }

            var mixture = new GasMixture(Atmospherics.CellVolume) { Temperature = Atmospherics.T20C };
            mixture.SetMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
            mixture.SetMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);

            server.EntMan.System<AtmosphereSystem>().SetMapAtmosphere(testMap.MapUid, space: false, mixture);
            server.EntMan.EnsureComponent<GridAtmosphereComponent>(testMap.Grid.Owner);
        });

        await server.WaitRunTicks(30);
        return testMap;
    }

    private EntityCoordinates TileCentre(TestMapData testMap, Vector2i tile)
        => new(testMap.Grid.Owner, tile.X + 0.5f, tile.Y + 0.5f);

    private TileRef TileRef(TestMapData testMap, Vector2i tile)
        => Pair.Server.EntMan.System<SharedMapSystem>().GetTileRef(testMap.Grid.Owner, testMap.Grid.Comp, tile);

    private bool HasElectricalFire(TestMapData testMap, Vector2i tile)
    {
        var chemicalFireSystem = Pair.Server.EntMan.System<SharedChemicalFireSystem>();
        if (!SProtoMan.Index(ElectricalFirePrototype).TryComp<ChemicalFireComponent>(out var prototypeComponent, Pair.Server.EntMan.ComponentFactory))
            return false;

        var connectionKey = prototypeComponent.ConnectionKey ?? ElectricalFirePrototype.Id;
        return chemicalFireSystem.TryGetChemicalFire((testMap.Grid.Owner, null), tile, connectionKey, out var fire) &&
               !Pair.Server.EntMan.IsQueuedForDeletion(fire.Owner);
    }

    /// <summary>
    ///     An arc flash sets fire to what threw it and to the machine its bolt hit, but not to a person it hit.
    /// </summary>
    [Test]
    public async Task ArcFlashSetsFireToMachines()
    {
        var testMap = await CreateRoom();
        var server = Pair.Server;

        await server.WaitAssertion(() =>
        {
            var entityManager = server.EntMan;
            var emitterUid = entityManager.SpawnEntity(_emitterPrototype, TileCentre(testMap, Centre));
            entityManager.SpawnEntity(_machinePrototype, TileCentre(testMap, East));
            entityManager.SpawnEntity(_bystanderPrototype, TileCentre(testMap, West));

            entityManager.EventBus.RaiseLocalEvent(emitterUid, new MachineDeconstructedEvent());

            using (Assert.EnterMultipleScope())
            {
                Assert.That(HasElectricalFire(testMap, Centre), "The arc flash did not set fire to what threw it.");
                Assert.That(HasElectricalFire(testMap, East), "The arc flash did not set fire to the machine it hit.");
                Assert.That(HasElectricalFire(testMap, West), Is.False, "The arc flash set fire to someone it hit.");
            }
        });
    }

    /// <summary>
    ///     No chemfire burns inside a wall. A machine on one has its fire on the tile it faces instead.
    /// </summary>
    [Test]
    public async Task FireOnAWallStartsInFrontOfIt()
    {
        var testMap = await CreateRoom();
        var server = Pair.Server;

        await server.WaitAssertion(() =>
        {
            var entityManager = server.EntMan;
            var chemicalFireSystem = entityManager.System<SharedChemicalFireSystem>();

            entityManager.SpawnEntity(WallPrototype, TileCentre(testMap, East));
            var mountedUid = entityManager.SpawnEntity(_machinePrototype, TileCentre(testMap, East));
            // Facing west, into the room.
            entityManager.System<SharedTransformSystem>().SetLocalRotation(mountedUid, Direction.West.ToAngle());

            using (Assert.EnterMultipleScope())
            {
                Assert.That(chemicalFireSystem.SpawnChemicalFire(ChemicalFirePrototype, TileRef(testMap, East)), Is.Null,
                    "A chemfire started inside a wall.");
                Assert.That(entityManager.System<ArcFlashSystem>().TryStartElectricalFire(mountedUid, ElectricalFirePrototype),
                    "No fire started for a machine on a wall.");
                Assert.That(HasElectricalFire(testMap, East), Is.False, "The machine's fire is inside the wall.");
                Assert.That(HasElectricalFire(testMap, Centre), "The machine's fire is not on the tile it faces.");
            }
        });
    }

    /// <summary>
    ///     Foam does nothing to an electrical fire, and water cannot cool the air enough; carbon dioxide puts it out.
    /// </summary>
    [Test]
    public async Task OnlyCarbonDioxidePutsItOut()
    {
        var testMap = await CreateRoom();
        var server = Pair.Server;
        var entityManager = server.EntMan;

        await server.WaitAssertion(() =>
        {
            Assert.That(entityManager.System<SharedChemicalFireSystem>().SpawnChemicalFire(ElectricalFirePrototype, TileRef(testMap, Centre)), Is.Not.Null);
            for (var i = 0; i < 30; i++)
                entityManager.System<KsFoamBlanketSystem>().Deploy(BlanketPrototype, TileRef(testMap, Centre), 1f, playSound: false);
        });

        await server.WaitRunTicks(TicksPerSecond * 2);
        await server.WaitAssertion(() => Assert.That(HasElectricalFire(testMap, Centre), "Foam put the electrical fire out."));

        await server.WaitAssertion(() =>
            entityManager.System<PuddleSystem>().DoTileReactions(TileRef(testMap, Centre),
                new Solution(WaterReagent.Id, FixedPoint2.New(20)) { Temperature = Atmospherics.T20C }));

        await server.WaitRunTicks(TicksPerSecond * 2);
        await server.WaitAssertion(() => Assert.That(HasElectricalFire(testMap, Centre), "Water put the electrical fire out."));

        await server.WaitAssertion(() =>
            entityManager.System<PuddleSystem>().DoTileReactions(TileRef(testMap, Centre),
                new Solution(CarbonDioxideReagent.Id, FixedPoint2.New(15)) { Temperature = 194f }));

        await server.WaitRunTicks(TicksPerSecond * 2);
        await server.WaitAssertion(() => Assert.That(HasElectricalFire(testMap, Centre), Is.False, "Carbon dioxide did not put the electrical fire out."));
    }

    /// <summary>
    ///     Whether a sound playing <paramref name="path"/> is on the test map.
    /// </summary>
    private bool IsPlaying(TestMapData testMap, string path)
    {
        var query = Pair.Server.EntMan.AllEntityQueryEnumerator<AudioComponent, TransformComponent>();
        while (query.MoveNext(out var audioComponent, out var transformComponent))
        {
            if (transformComponent.MapID == testMap.MapId && audioComponent.FileName == path)
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Foam on an electrical fire does not sizzle, since it does nothing; the fire roars on instead. Carbon dioxide
    ///         putting it out does sizzle.
    /// </summary>
    [Test]
    public async Task SoundsSayWhetherItWentOut()
    {
        var testMap = await CreateRoom();
        var server = Pair.Server;
        var entityManager = server.EntMan;
        var sizzlePath = string.Empty;

        await server.WaitAssertion(() =>
        {
            var fire = entityManager.System<SharedChemicalFireSystem>().SpawnChemicalFire(ElectricalFirePrototype, TileRef(testMap, Centre));
            Assert.That(fire, Is.Not.Null);
            sizzlePath = ((SoundPathSpecifier)fire!.Value.Comp.ExtinguishSound!).Path.ToString();
            var roarPath = ((SoundPathSpecifier)fire.Value.Comp.FoamResistSound!).Path.ToString();

            entityManager.System<KsFoamBlanketSystem>().Deploy(BlanketPrototype, TileRef(testMap, Centre), 1f, playSound: true);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(IsPlaying(testMap, sizzlePath), Is.False, "Foam sizzled on a fire it does nothing to.");
                Assert.That(IsPlaying(testMap, roarPath), "The fire did not roar on under the foam.");
            }
        });

        await server.WaitAssertion(() =>
            entityManager.System<PuddleSystem>().DoTileReactions(TileRef(testMap, Centre),
                new Solution(CarbonDioxideReagent.Id, FixedPoint2.New(15)) { Temperature = 194f }));

        var sizzled = false;
        for (var tick = 0; tick < TicksPerSecond * 2 && !sizzled; tick++)
        {
            await server.WaitRunTicks(1);
            await server.WaitPost(() => sizzled = IsPlaying(testMap, sizzlePath));
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(HasElectricalFire(testMap, Centre), Is.False, "Carbon dioxide did not put the electrical fire out.");
            Assert.That(sizzled, "Carbon dioxide put the electrical fire out without a sizzle.");
        });
    }

    /// <summary>
    ///     Foam over an electrical fire does not put it out, and does not keep what lies in it safe for long either: the
    ///         fire boils it off, then sets alight what it was protecting.
    /// </summary>
    [Test]
    public async Task FoamBurnsAwayOverIt()
    {
        var testMap = await CreateRoom();
        var server = Pair.Server;
        var entityManager = server.EntMan;
        var blanketSystem = entityManager.System<KsFoamBlanketSystem>();
        EntityUid flammableUid = default;

        await server.WaitAssertion(() =>
        {
            Assert.That(entityManager.System<SharedChemicalFireSystem>().SpawnChemicalFire(ElectricalFirePrototype, TileRef(testMap, Centre)), Is.Not.Null);
            for (var i = 0; i < 30; i++)
                blanketSystem.Deploy(BlanketPrototype, TileRef(testMap, Centre), 1f, playSound: false);

            Assert.That(blanketSystem.TryGetBlanket(testMap.Grid.Owner, Centre, out var blanket) && blanket.Comp.Foam >= blanket.Comp.MaxFoam - 0.02f,
                "The blanket did not fill up.");
            flammableUid = entityManager.SpawnEntity(_flammablePrototype, TileCentre(testMap, Centre));
        });

        // A full blanket drains by itself in well over a minute.
        await server.WaitRunTicks(TicksPerSecond * 4);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(blanketSystem.TryGetBlanket(testMap.Grid.Owner, Centre, out _), Is.False, "The electrical fire did not burn the foam off.");
                Assert.That(HasElectricalFire(testMap, Centre), "The foam put the electrical fire out.");
                Assert.That(entityManager.GetComponent<FlammableComponent>(flammableUid).OnFire, "What lay in the foam did not catch fire once it was gone.");
            }
        });
    }

    /// <summary>
    ///     Left alone in a room with air, an electrical fire does not burn out.
    /// </summary>
    [Test]
    public async Task BurnsUntilPutOut()
    {
        var testMap = await CreateRoom();
        var server = Pair.Server;

        await server.WaitAssertion(() =>
            Assert.That(server.EntMan.System<SharedChemicalFireSystem>().SpawnChemicalFire(ElectricalFirePrototype, TileRef(testMap, Centre)), Is.Not.Null));

        await server.WaitRunTicks(TicksPerSecond * 60);

        await server.WaitAssertion(() => Assert.That(HasElectricalFire(testMap, Centre), "The electrical fire burnt out by itself."));
    }
}
