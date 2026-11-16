using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Tests.Movement;
using Content.Server.Atmos.Components;
using Content.Server.Zombies;
using Content.Shared._KS14.Lawnmower;
using Content.Shared.Buckle;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Emag.Systems;
using Content.Shared.Inventory;
using Content.Shared.Maps;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Standing;
using Content.Shared.VendingMachines;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Lawnmower;

[TestOf(typeof(SharedKsLawnmowerSystem))]
public sealed class KsLawnmowerTest : MovementTest
{
    private static readonly ProtoId<DamageTypePrototype> Blunt = "Blunt";
    private static readonly EntProtoId Mower = "KsLawnmower";
    private static readonly EntProtoId EmaggedMower = "KsLawnmowerEmagged";
    private static readonly EntProtoId Human = "MobHuman";
    private static readonly EntProtoId Shorts = "KsClothingUniformGrillingShorts";
    private static readonly EntProtoId Emag = "EmagUnlimited";
    private static readonly EntProtoId ChefJumpsuit = "ClothingUniformJumpsuitChef";
    private static readonly ProtoId<LoadoutGroupPrototype> ChefJumpsuitGroup = "ChefJumpsuit";
    private static readonly ProtoId<LoadoutPrototype> ShortsLoadout = "KsGrillingShorts";
    private static readonly ProtoId<LoadoutPrototype> StandardLoadout = "ChefJumpsuit";
    private static readonly ProtoId<VendingMachineInventoryPrototype> ChefDrobe = "ChefDrobeInventory";
    private static readonly ProtoId<ContentTileDefinition> AstroGrass = "FloorAstroGrass";
    private static readonly ProtoId<ContentTileDefinition> MowedAstroGrass = "FloorMowedAstroGrass";

    protected override int Tiles => 6;

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task DrivingMowsGrassAndOnlyGibsProneZombies(bool emagged, bool zombie)
    {
        var origin = SEntMan.GetCoordinates(PlayerCoords);
        var mowerUid = await SpawnEntity(emagged ? EmaggedMower : Mower, origin);
        var victimUid = await SpawnEntity(Human, origin.Offset(new Vector2(2, 0)));
        var mapSystem = Server.System<SharedMapSystem>();
        var grassIndices = mapSystem.TileIndicesFor(MapData.Grid, origin.Offset(new Vector2(1, 0)));

        await Server.WaitAssertion(() =>
        {
            mapSystem.SetTile(MapData.Grid, grassIndices, new Tile(ProtoMan.Index(AstroGrass).TileId));
            // Isolate mower damage from decompression on the open test grid.
            SEntMan.RemoveComponent<BarotraumaComponent>(victimUid);
            if (zombie)
                Server.System<ZombieSystem>().ZombifyEntity(victimUid);

            Server.System<StandingStateSystem>().Down(victimUid, playSound: false, force: true);
            Assert.That(Server.System<SharedBuckleSystem>().TryBuckle(SPlayer, SPlayer, mowerUid), Is.True);
        });
        await RunTicks(10);

        await Move(DirectionFlag.East, 1.1f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(Transform.GetWorldPosition(mowerUid).X, Is.GreaterThan(Transform.ToWorldPosition(origin).X + 2f),
                "The player's movement input should drive the mower past the target.");
            Assert.That(mapSystem.GetTileRef(MapData.Grid, grassIndices).Tile.TypeId, Is.EqualTo(ProtoMan.Index(MowedAstroGrass).TileId),
                "Driving over artificial grass should mow the tile.");

            if (zombie)
            {
                Assert.That(SEntMan.EntityExists(victimUid), Is.False, "The emagged mower should gib a prone zombie.");
                return;
            }

            Assert.That(SEntMan.EntityExists(victimUid), Is.True, "Humanoid bodies should remain intact.");
            Assert.That(Server.System<MobStateSystem>().IsDead(victimUid), Is.EqualTo(emagged));
            if (!emagged)
                Assert.That(GetBluntDamage(victimUid), Is.EqualTo(FixedPoint2.Zero));
        });
    }

    [Test]
    public async Task EmaggedMowerShovesAStandingTarget()
    {
        var origin = SEntMan.GetCoordinates(PlayerCoords);
        var mowerUid = await SpawnEntity(EmaggedMower, origin);
        var victimUid = await SpawnEntity(Human, origin.Offset(new Vector2(2, 0)));

        await Server.WaitAssertion(() =>
        {
            SEntMan.RemoveComponent<BarotraumaComponent>(victimUid);
            Assert.That(Server.System<SharedBuckleSystem>().TryBuckle(SPlayer, SPlayer, mowerUid), Is.True);
        });
        await RunTicks(10);
        await Move(DirectionFlag.East, 0.65f);

        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<MobStateSystem>().IsAlive(victimUid), Is.True);
            Assert.That(GetBluntDamage(victimUid), Is.GreaterThan(FixedPoint2.Zero));
            Assert.That(Transform.GetWorldPosition(victimUid).X, Is.GreaterThan(Transform.ToWorldPosition(origin).X + 2f));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task GrillingShortsBoostTheCurrentRiderAndEmagWorks(bool preEmagged)
    {
        var origin = SEntMan.GetCoordinates(PlayerCoords);
        var mowerUid = await SpawnEntity(preEmagged ? EmaggedMower : Mower, origin);
        var shortsUid = await SpawnEntity(Shorts, origin);
        var emagUid = await SpawnEntity(Emag, origin);

        await Server.WaitAssertion(() =>
        {
            var emagSystem = Server.System<EmagSystem>();
            Assert.That(emagSystem.TryEmagEffect(emagUid, SPlayer, mowerUid), Is.EqualTo(!preEmagged));
            Assert.That(emagSystem.CheckFlag(mowerUid, EmagType.Interaction), Is.True);
            Assert.That(Server.System<SharedBuckleSystem>().TryBuckle(SPlayer, SPlayer, mowerUid), Is.True);

            var speed = SEntMan.GetComponent<MovementSpeedModifierComponent>(mowerUid);
            var baseSpeed = speed.CurrentSprintSpeed;
            var inventory = Server.System<InventorySystem>();
            Assert.That(inventory.TryEquip(SPlayer, shortsUid, "jumpsuit", silent: true, force: true), Is.True);
            Assert.That(speed.CurrentSprintSpeed, Is.EqualTo(baseSpeed * SEntMan.GetComponent<KsGrillingShortsComponent>(shortsUid).MowerSpeedMultiplier).Within(0.001f));

            Assert.That(inventory.TryUnequip(SPlayer, "jumpsuit", silent: true, force: true), Is.True);
            Assert.That(speed.CurrentSprintSpeed, Is.EqualTo(baseSpeed).Within(0.001f));
        });
    }

    private FixedPoint2 GetBluntDamage(EntityUid victimUid)
    {
        var damageable = SEntMan.GetComponent<DamageableComponent>(victimUid);
        var damage = Server.System<DamageableSystem>().GetPositiveDamage((victimUid, damageable));
        return damage.DamageDict.GetValueOrDefault(Blunt);
    }

    [Test]
    public void ChefClothingAddsShortsAlongsideStandardOptions()
    {
        var chefUniforms = ProtoMan.Index(ChefJumpsuitGroup).Loadouts;
        var chefVendorInventory = ProtoMan.Index(ChefDrobe).StartingInventory;
        Assert.Multiple(() =>
        {
            Assert.That(chefUniforms, Does.Contain(ShortsLoadout).And.Contain(StandardLoadout));
            Assert.That(chefVendorInventory, Does.ContainKey(Shorts).And.ContainKey(ChefJumpsuit));
        });
    }
}
