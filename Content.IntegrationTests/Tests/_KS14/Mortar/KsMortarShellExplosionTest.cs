#nullable enable
using System;
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
///     Content: a mortar shell's blast (<c>KsMortarShell</c>) hurts what it lands by as any ordinary explosion does:
///         sandbags and turrets, and the map's walls and windows too.
/// </summary>
[TestFixture]
public sealed class KsMortarShellExplosionTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private static readonly EntProtoId MortarShell = "KsMortarShell";
    private static readonly EntProtoId SandbagWall = "KsSandbagWall";
    private static readonly EntProtoId Turret = "KsWeaponTurretNanoTrasenDisposable";
    private static readonly EntProtoId StationWall = "WallSolid";
    private static readonly EntProtoId StationWindow = "Window";

    [Test]
    public async Task ItHurtsFortificationsTurretsWallsAndWindows()
    {
        var testMap = await Pair.CreateTestMap();
        EntityUid sandbagUid = default, turretUid = default, wallUid = default, windowUid = default;

        await Server.WaitPost(() =>
        {
            var explosionSystem = SEntMan.System<ExplosionSystem>();

            // As the shell sets it off, from its own prototype, and how far that reaches.
            SProtoMan.Index(MortarShell).TryGetComponent<Content.Shared.Trigger.Components.Effects.ExplosionOnTriggerComponent>(out var trigger, SEntMan.ComponentFactory);
            Assert.That(trigger, Is.Not.Null);
            var radius = explosionSystem.IntensityToRadius(trigger.TotalIntensity, trigger.IntensitySlope, trigger.MaxTileIntensity);
            Assert.That(radius, Is.GreaterThanOrEqualTo(1f), "the shell's blast should reach past its own tile");

            // Well inside the blast; and the second blast, by the wall and the window, far enough off that neither reaches
            //      the other's targets, nor the turret's own blast, a blast of another kind, the wall or the window.
            var near = Math.Max(1, (int)MathF.Floor(radius / 2f));
            var turretRadius = 0f;
            if (SProtoMan.Index(Turret).TryGetComponent<Content.Shared.Explosion.Components.ExplosiveComponent>(out var turretExplosive, SEntMan.ComponentFactory))
                turretRadius = explosionSystem.IntensityToRadius(turretExplosive.TotalIntensity, turretExplosive.IntensitySlope, turretExplosive.MaxIntensity);

            var apart = (int)MathF.Ceiling(2f * radius + turretRadius) + near + 2;
            KsFieldCommanderTestHelpers.FillFloor(Server, testMap, halfExtent: apart + near + 2);

            sandbagUid = SEntMan.SpawnEntity(SandbagWall, new EntityCoordinates(testMap.Grid.Owner, near + 0.5f, 0.5f));
            turretUid = SEntMan.SpawnEntity(Turret, new EntityCoordinates(testMap.Grid.Owner, 0.5f, near + 0.5f));
            wallUid = SEntMan.SpawnEntity(StationWall, new EntityCoordinates(testMap.Grid.Owner, apart + near + 0.5f, 0.5f));
            windowUid = SEntMan.SpawnEntity(StationWindow, new EntityCoordinates(testMap.Grid.Owner, apart + 0.5f, near + 0.5f));

            foreach (var x in new[] { 0.5f, apart + 0.5f })
            {
                explosionSystem.QueueExplosion(new MapCoordinates(new System.Numerics.Vector2(x, 0.5f), testMap.MapId),
                    trigger!.ExplosionType.Id, trigger.TotalIntensity, trigger.IntensitySlope, trigger.MaxTileIntensity, cause: null,
                    maxTileBreak: trigger.MaxTileBreak, canCreateVacuum: false);
            }
        });

        await Pair.RunTicksSync(60);

        await Server.WaitAssertion(() =>
        {
            Assert.That(IsHurt(sandbagUid), Is.True, "the sandbag wall should be hurt");
            Assert.That(IsHurt(turretUid), Is.True, "the turret should be hurt");
            Assert.That(IsHurt(wallUid), Is.True, "the station wall should be hurt");
            Assert.That(IsHurt(windowUid), Is.True, "the window should be hurt");
        });
    }

    private bool IsHurt(EntityUid uid)
    {
        return !SEntMan.EntityExists(uid) ||
               SEntMan.HasComponent<DamageableComponent>(uid) && SEntMan.System<DamageableSystem>().GetTotalDamage(uid) > FixedPoint2.Zero;
    }
}
