#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.Components;
using Content.Server._KS14.NPC.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     Ranged attack patterns (<see cref="NpcCombatRangedPatternSystem"/>): bursts, their aim, cooldowns, and keeping
///         the patterns out of the spawn menu.
/// </summary>
public sealed class KsNpcRangedPatternTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private const string ShooterMob = "KsPatternTestShooter";
    private const string Bolt = "KsPatternTestBolt";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: BaseBullet
  id: KsPatternTestBolt

- type: entity
  parent: BaseNPCRanged
  id: KsPatternTestCross
  components:
  - type: NpcRangedAttackPattern
    attackType: CardinalDirections
    projectile: KsPatternTestBolt
    shots: 4
    burstCount: 2
    shotDelay: 0.2
    cooldown: 30

- type: entity
  id: KsPatternTestShooter
  components:
  - type: NpcRangedAttackPatternHolder
    attacks:
      cross: KsPatternTestCross
";

    /// <summary>
    ///     An attack fires all its bursts - the cardinal directions, then the diagonals - and cannot be started again
    ///         while it is firing, nor once it has finished, until its cooldown is over.
    /// </summary>
    [Test]
    public async Task TestAttackFiresItsBurstsThenCoolsDown()
    {
        var tileDefinitionManager = Server.ResolveDependency<ITileDefinitionManager>();
        var patternSystem = SEntMan.System<NpcCombatRangedPatternSystem>();
        var map = await Pair.CreateTestMap();
        EntityUid shooterUid = default;

        await Server.WaitPost(() =>
        {
            var gridUid = MakeGrid(SEntMan, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-2, -2), new Vector2i(2, 2)).Owner;
            shooterUid = SEntMan.SpawnEntity(ShooterMob, new EntityCoordinates(gridUid, new Vector2(0.5f, 0.5f)));

            Assert.That(patternSystem.TryStartAttack(shooterUid, "cross", targetUid: null), "the attack should start");
            Assert.That(patternSystem.IsAttackActive(shooterUid));
            Assert.That(patternSystem.TryStartAttack(shooterUid, "cross", targetUid: null), Is.False, "nor start again while firing");
        });

        await Pair.RunTicksSync(30);

        await Server.WaitAssertion(() =>
        {
            var bolts = new List<Vector2>();
            var physicsEnumerator = SEntMan.EntityQueryEnumerator<MetaDataComponent, PhysicsComponent>();
            while (physicsEnumerator.MoveNext(out _, out var metaDataComponent, out var physicsComponent))
            {
                if (metaDataComponent.EntityPrototype?.ID == Bolt)
                    bolts.Add(physicsComponent.LinearVelocity);
            }

            Assert.Multiple(() =>
            {
                Assert.That(bolts, Has.Count.EqualTo(8), "two bursts of four");
                Assert.That(bolts.Count(IsDiagonal), Is.EqualTo(4), "the second burst along the diagonals");
                Assert.That(patternSystem.IsAttackActive(shooterUid), Is.False, "and then it is done");
                Assert.That(patternSystem.TryStartAttack(shooterUid, "cross", targetUid: null), Is.False, "and on cooldown");
            });
        });
    }

    private static bool IsDiagonal(Vector2 velocity)
    {
        return MathF.Abs(velocity.X) > 0.1f && MathF.Abs(velocity.Y) > 0.1f;
    }

    /// <summary>
    ///     Attack patterns are data, never spawned, so none of them shows in the spawn menu - which is built on the
    ///         client, where their component, being server-only, is unknown.
    /// </summary>
    [Test]
    public async Task TestAttackPatternsAreHiddenFromSpawnMenu()
    {
        var serverPrototypeManager = Server.ResolveDependency<IPrototypeManager>();
        var clientPrototypeManager = Client.ResolveDependency<IPrototypeManager>();
        var componentFactory = Server.ResolveDependency<IComponentFactory>();

        var patternIds = serverPrototypeManager.EnumeratePrototypes<EntityPrototype>()
            .Where(prototype => !prototype.Abstract && prototype.TryComp<NpcRangedAttackPatternComponent>(out _, componentFactory))
            .Select(prototype => prototype.ID)
            .ToList();

        Assert.That(patternIds, Is.Not.Empty);

        Assert.Multiple(() =>
        {
            foreach (var patternId in patternIds)
            {
                Assert.That(clientPrototypeManager.Index<EntityPrototype>(patternId).HideSpawnMenu, $"{patternId} should be hidden from the spawn menu");
            }
        });

        await Task.CompletedTask;
    }
}
