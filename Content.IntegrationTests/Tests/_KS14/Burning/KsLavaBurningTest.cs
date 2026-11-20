#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Gravity;
using Content.Shared._KS14.Burning;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Gravity;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Burning;

[TestFixture]
[TestOf(typeof(KsItemBurningSystem))]
public sealed class KsLavaBurningTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Dirty = true };

    private static readonly EntProtoId Lava = "FloorLavaEntity";
    private static readonly EntProtoId LiquidPlasma = "FloorLiquidPlasmaEntity";
    private static readonly EntProtoId WaterTile = "FloorWaterEntity";
    private static readonly EntProtoId FireExtinguisher = "FireExtinguisher";
    private const string Water = "Water";
    private const string WeldingFuel = "WeldingFuel";
    private static readonly EntProtoId Ash = "KsAsh";
    private static readonly EntProtoId UnfiredSpear = "KsSpearGiantslayerUnfired";
    private static readonly EntProtoId FiredSpear = "KsSpearGiantslayer";
    private readonly EntProtoId _plainItem = "KsTestLavaItem";
    private readonly EntProtoId _lavaProofItem = "KsTestLavaProofItem";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: BaseItem
  id: KsTestLavaItem

- type: entity
  parent: BaseItem
  id: KsTestLavaProofItem
  components:
  - type: KsLavaProof
";

    private Task<(EntityUid ItemUid, EntityCoordinates Coordinates)> DropInLavaAsync(EntProtoId item)
    {
        return DropInAsync(Lava, item);
    }

    private async Task<(EntityUid ItemUid, EntityCoordinates Coordinates)> DropInAsync(EntProtoId? tile, EntProtoId item)
    {
        var testMap = await Pair.CreateTestMap();
        var coordinates = new EntityCoordinates(testMap.Grid, 0.5f, 0.5f);
        var itemUid = EntityUid.Invalid;
        await Server.WaitPost(() =>
        {
            // Lava, like any step trigger, ignores weightless items; real lava sits on a planet with gravity.
            SEntMan.EnsureComponent<GravityComponent>(testMap.Grid);
            SEntMan.System<GravitySystem>().EnableGravity(testMap.Grid);
            if (tile != null)
                SEntMan.SpawnEntity(tile, coordinates);

            itemUid = SEntMan.SpawnEntity(item, coordinates);
        });
        await Pair.RunTicksSync(10);
        return (itemUid, coordinates);
    }

    private int TicksFor(float seconds) => (int)(seconds * (float)Server.ResolveDependency<IGameTiming>().TickRate);

    [Test]
    public async Task ItemsInLavaBurnSlowlyToAsh()
    {
        var (itemUid, _) = await DropInLavaAsync(_plainItem);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<KsItemBurningComponent>(itemUid), Is.True, "An item in lava must catch fire.");
            Assert.That(SEntMan.Deleted(itemUid), Is.False, "It must burn slowly, not vanish.");
        });

        // 200 integrity: 10 at once, then 5 a second.
        await Pair.RunTicksSync(TicksFor(45f));

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.Deleted(itemUid), Is.True, "The item must have burned away.");
            Assert.That(SEntMan.EntityQuery<MetaDataComponent>().Any(meta => meta.EntityPrototype?.ID == Ash.Id), Is.True,
                "Burning away must leave ash.");
        });
    }

    [Test]
    public async Task LavaProofItemsDoNotBurn()
    {
        var (itemUid, _) = await DropInLavaAsync(_lavaProofItem);
        await Pair.RunTicksSync(TicksFor(5f));

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.Deleted(itemUid), Is.False);
            Assert.That(SEntMan.HasComponent<KsItemBurningComponent>(itemUid), Is.False, "A lava-proof item must not catch fire.");
        });
    }

    /// <summary>
    ///     Liquid plasma is lava as far as a loose item is concerned.
    /// </summary>
    [Test]
    public async Task ItemsInLiquidPlasmaCatchFire()
    {
        var (itemUid, _) = await DropInAsync(LiquidPlasma, _plainItem);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<KsItemBurningComponent>(itemUid), Is.True, "An item in liquid plasma must catch fire.");
        });
    }

    /// <summary>
    ///     What a fire extinguisher's vapor does to whatever it touches: a touch reaction with its water.
    /// </summary>
    [Test]
    public async Task SprayedWaterPutsABurningItemOut()
    {
        var (itemUid, _) = await DropInAsync(null, _plainItem);

        await Server.WaitAssertion(() =>
        {
            SEntMan.System<KsItemBurningSystem>().Ignite(itemUid);
            Assert.That(SEntMan.HasComponent<KsItemBurningComponent>(itemUid), Is.True, "The item never caught fire, so putting it out proves nothing.");

            SEntMan.System<ReactiveSystem>().ReactionEntity(itemUid, ReactionMethod.Touch, new ReagentQuantity(WeldingFuel, FixedPoint2.New(5)));
            Assert.That(SEntMan.HasComponent<KsItemBurningComponent>(itemUid), Is.True, "Welding fuel must not put a burning item out.");

            SEntMan.System<ReactiveSystem>().ReactionEntity(itemUid, ReactionMethod.Touch, new ReagentQuantity(Water, FixedPoint2.New(5)));
        });

        // It goes out at the end of the tick.
        await Pair.RunTicksSync(2);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<KsItemBurningComponent>(itemUid), Is.False, "Water must put a burning item out.");
        });
    }

    /// <summary>
    ///     A burning item goes out in a water tile, and keeps burning on dry floor.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task WaterTilePutsABurningItemOut(bool inWater)
    {
        var (itemUid, coordinates) = await DropInAsync(null, _plainItem);

        await Server.WaitAssertion(() =>
        {
            SEntMan.System<KsItemBurningSystem>().Ignite(itemUid);
            Assert.That(SEntMan.HasComponent<KsItemBurningComponent>(itemUid), Is.True, "The item never caught fire, so putting it out proves nothing.");

            if (inWater)
                SEntMan.SpawnEntity(WaterTile, coordinates);
        });

        await Pair.RunTicksSync(TicksFor(3f));

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.Deleted(itemUid), Is.False);
            Assert.That(SEntMan.HasComponent<KsItemBurningComponent>(itemUid), Is.EqualTo(!inWater),
                inWater ? "A water tile must put a burning item out." : "A burning item on dry floor must keep burning.");
        });
    }

    /// <summary>
    ///     The real fire extinguisher, shot along a row of floor with a burning item on it: the shot itself has to
    ///         hit the item. Its shots collide with next to nothing, a loose item included, so a burning item carries
    ///         a fixture for them to hit.
    /// </summary>
    [Test]
    public async Task FireExtinguisherPutsABurningItemOut()
    {
        var (itemUid, coordinates) = await DropInAsync(null, _plainItem);
        var gridUid = coordinates.EntityId;
        var extinguisherUid = EntityUid.Invalid;
        var gunSystem = SEntMan.System<SharedGunSystem>();

        await Server.WaitAssertion(() =>
        {
            // Foam needs air to lie in.
            var mixture = new GasMixture(Atmospherics.CellVolume) { Temperature = Atmospherics.T20C };
            mixture.SetMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
            mixture.SetMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);
            SEntMan.System<AtmosphereSystem>().SetMapAtmosphere(SEntMan.GetComponent<TransformComponent>(gridUid).MapUid!.Value, space: false, mixture);
            SEntMan.EnsureComponent<GridAtmosphereComponent>(gridUid);

            var mapSystem = SEntMan.System<SharedMapSystem>();
            var mapGridComponent = SEntMan.GetComponent<MapGridComponent>(gridUid);
            var floorTile = mapSystem.GetTileRef(gridUid, mapGridComponent, Vector2i.Zero).Tile;
            for (var x = 1; x <= 4; x++)
                mapSystem.SetTile(gridUid, mapGridComponent, new Vector2i(x, 0), floorTile);

            // One tile down the row from the muzzle.
            SEntMan.System<SharedTransformSystem>().SetCoordinates(itemUid, new EntityCoordinates(gridUid, 1.5f, 0.5f));
            SEntMan.System<KsItemBurningSystem>().Ignite(itemUid);
            Assert.That(SEntMan.HasComponent<KsItemBurningComponent>(itemUid), Is.True, "The item never caught fire, so putting it out proves nothing.");

            extinguisherUid = SEntMan.SpawnEntity(FireExtinguisher, coordinates);
            SEntMan.System<ItemToggleSystem>().TryActivate(extinguisherUid, user: null); // safety off
        });

        await Pair.RunTicksSync(TicksFor(1f));

        // A held trigger asks to shoot every tick.
        var shotTarget = new EntityCoordinates(gridUid, 4.5f, 0.5f);
        for (var tick = 0; tick < TicksFor(1f); tick++)
        {
            await Server.WaitPost(() =>
            {
                var gun = new Entity<GunComponent>(extinguisherUid, SEntMan.GetComponent<GunComponent>(extinguisherUid));
                gunSystem.AttemptShoot(extinguisherUid, gun, shotTarget);
            });

            await Pair.RunTicksSync(1);
        }

        await Pair.RunTicksSync(TicksFor(2f));

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.Deleted(itemUid), Is.False);
            Assert.That(SEntMan.HasComponent<KsItemBurningComponent>(itemUid), Is.False, "A fire extinguisher must put a burning item out.");
            Assert.That(SEntMan.System<FixtureSystem>().GetFixtureOrNull(itemUid, KsItemBurningSystem.SprayTargetFixtureId), Is.Null,
                "An item that is out must lose the fixture the spray hit it by.");
        });
    }

    [Test]
    public async Task UnfiredSpearIsForgedByLava()
    {
        var (spearUid, _) = await DropInLavaAsync(UnfiredSpear);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.Deleted(spearUid), Is.True, "The unfired spear must be replaced.");
            Assert.That(SEntMan.EntityQuery<MetaDataComponent>().Any(meta => meta.EntityPrototype?.ID == FiredSpear.Id), Is.True,
                "Lava must forge it into the finished giantslayer spear.");
        });
    }
}
