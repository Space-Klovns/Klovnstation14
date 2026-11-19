#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests._KS14.FieldCommander;
using Content.Server.Explosion.EntitySystems;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Explosion;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.Mortar;

/// <summary>
///     Content: a mortar shell's blast (<c>KsMortarShell</c>) takes sandbags and turrets apart and leaves the map's walls
///         standing.
/// </summary>
[TestFixture]
public sealed class KsMortarShellExplosionTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private static readonly ProtoId<ExplosionPrototype> MortarShellExplosion = "KsMortarShell";
    private static readonly EntProtoId MortarShell = "KsMortarShell";
    private static readonly EntProtoId SandbagWall = "KsSandbagWall";
    private static readonly EntProtoId Turret = "KsWeaponTurretNanoTrasenDisposable";
    private static readonly EntProtoId StationWall = "WallSolid";

    [Test]
    public async Task ItHurtsFortificationsAndTurretsButNotWalls()
    {
        var testMap = await Pair.CreateTestMap();
        EntityUid sandbagUid = default, turretUid = default, wallUid = default;

        await Server.WaitPost(() =>
        {
            KsFieldCommanderTestHelpers.FillFloor(Server, testMap, halfExtent: 30);
            sandbagUid = SEntMan.SpawnEntity(SandbagWall, new EntityCoordinates(testMap.Grid.Owner, 2.5f, 0.5f));
            turretUid = SEntMan.SpawnEntity(Turret, new EntityCoordinates(testMap.Grid.Owner, 0.5f, 2.5f));
            // Far from the turret, whose own destruction is a blast of another kind.
            wallUid = SEntMan.SpawnEntity(StationWall, new EntityCoordinates(testMap.Grid.Owner, 21.5f, 0.5f));

            // As the shell sets it off, from its own prototype.
            SProtoMan.Index(MortarShell).TryGetComponent<Content.Shared.Trigger.Components.Effects.ExplosionOnTriggerComponent>(out var trigger, SEntMan.ComponentFactory);
            Assert.That(trigger, Is.Not.Null);
            Assert.That(trigger!.ExplosionType, Is.EqualTo(MortarShellExplosion), "the shell should go off as its own blast");
            foreach (var x in new[] { 0.5f, 20.5f })
            {
                SEntMan.System<ExplosionSystem>().QueueExplosion(new MapCoordinates(new System.Numerics.Vector2(x, 0.5f), testMap.MapId),
                    MortarShellExplosion.Id, trigger.TotalIntensity, trigger.IntensitySlope, trigger.MaxTileIntensity, cause: null,
                    maxTileBreak: trigger.MaxTileBreak, canCreateVacuum: false);
            }
        });

        await Pair.RunTicksSync(60);

        await Server.WaitAssertion(() =>
        {
            Assert.That(IsHurt(sandbagUid), Is.True, "the sandbag wall should be hurt");
            Assert.That(IsHurt(turretUid), Is.True, "the turret should be hurt");
            Assert.That(SEntMan.EntityExists(wallUid) && !IsHurt(wallUid), Is.True, "the station wall should be untouched");
        });
    }

    private bool IsHurt(EntityUid uid)
    {
        return !SEntMan.EntityExists(uid) ||
               SEntMan.HasComponent<DamageableComponent>(uid) && SEntMan.System<DamageableSystem>().GetTotalDamage(uid) > FixedPoint2.Zero;
    }
}
