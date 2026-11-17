#nullable enable
using System;
using System.Threading.Tasks;
using Content.Client._KS14.ComplexShove;
using Content.IntegrationTests.Fixtures;
using Content.Server.Gravity;
using Content.Shared._KS14.ComplexShove;
using Content.Shared.CombatMode;
using Content.Shared.Gravity;
using Content.Shared.Weapons.Melee;
using NUnit.Framework;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._KS14.ComplexShove;

/// <summary>
///     Someone staggered by a shove sways on a client that sees them, for as long as the stagger lasts, and their
///         sprite is back where it was once it is over.
/// </summary>
[TestFixture]
[TestOf(typeof(KsStaggerVisualsSystem))]
public sealed class KsStaggerVisualsTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static readonly EntProtoId Human = "MobHuman";

    [Test]
    public async Task StaggeredTargetSwaysUntilItPasses()
    {
        var testMap = await Pair.CreateTestMap();
        EntityUid shoverUid = default;
        EntityUid targetUid = default;

        await Server.WaitPost(() =>
        {
            var mapSystem = SEntMan.System<SharedMapSystem>();
            for (var x = -1; x < 9; x++)
            {
                mapSystem.SetTile(testMap.Grid.Owner, testMap.Grid.Comp, new Vector2i(x, 0), testMap.Tile.Tile);
            }

            var gravityComponent = SEntMan.EnsureComponent<GravityComponent>(testMap.Grid.Owner);
            SEntMan.System<GravitySystem>().EnableGravity(testMap.Grid.Owner, gravityComponent);

            shoverUid = SEntMan.SpawnEntity(Human, new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            targetUid = SEntMan.SpawnEntity(Human, new EntityCoordinates(testMap.Grid, 1.5f, 0.5f));
            Server.PlayerMan.SetAttachedEntity(Pair.Player!, shoverUid);
            SEntMan.System<SharedCombatModeSystem>().SetInCombatMode(shoverUid, true);
        });

        await Pair.RunTicksSync(10);

        var clientTargetUid = CEntMan.GetEntity(SEntMan.GetNetEntity(targetUid));
        var restOffset = default(System.Numerics.Vector2);
        var staggerSeconds = 0d;

        await Client.WaitAssertion(() =>
        {
            restOffset = CEntMan.GetComponent<SpriteComponent>(clientTargetUid).Offset;
            Assert.That(CEntMan.HasComponent<KsStaggerSwayComponent>(clientTargetUid), Is.False, "Nobody sways before being shoved.");
        });

        // Nothing behind them: the shove pushes and staggers.
        await Server.WaitAssertion(() =>
        {
            staggerSeconds = SEntMan.GetComponent<ComplexShoveComponent>(shoverUid).StaggerTime.TotalSeconds;
            var meleeSystem = SEntMan.System<SharedMeleeWeaponSystem>();
            Assert.That(meleeSystem.TryGetWeapon(shoverUid, out var weaponUid, out var weapon));
            meleeSystem.AttemptDisarmAttack(shoverUid, weaponUid, weapon!, targetUid);
        });

        await Pair.RunTicksSync(10);

        await Client.WaitAssertion(() =>
            Assert.That(CEntMan.HasComponent<KsStaggerSwayComponent>(clientTargetUid), "Someone staggered must sway on the client."));

        var timing = Server.ResolveDependency<IGameTiming>();
        await Pair.RunTicksSync((int)Math.Ceiling((staggerSeconds + 1) * timing.TickRate));

        await Client.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(CEntMan.HasComponent<KsStaggerSwayComponent>(clientTargetUid), Is.False,
                    "The sway must end with the stagger.");
                Assert.That(CEntMan.GetComponent<SpriteComponent>(clientTargetUid).Offset, Is.EqualTo(restOffset),
                    "Their sprite must be back where it was.");
            });
        });
    }
}
