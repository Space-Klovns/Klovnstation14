#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.NPC.Recovery;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.UnitTesting.Pool;
using static Content.IntegrationTests.Tests._KS14.NPC.KsNpcSquadTestHelpers;

namespace Content.IntegrationTests.Tests._KS14.NPC;

/// <summary>
///     <see cref="NpcRecoverySystem"/>: how long an NPC waits for its meds to work. What it does meanwhile is the operative
///         HTN's, in <see cref="KsOperativeHtnRootTest"/>.
/// </summary>
public sealed class KsNpcRecoveryTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private static readonly ProtoId<DamageTypePrototype> BluntDamage = "Blunt";

    private const string RecoveringMob = "KsRecoveryTestMob";
    private const string ImpatientMob = "KsRecoveryTestMobImpatient";

    /// <summary>
    ///     Critical at 100 damage, by its parent, so back to normal at 50 or less.
    /// </summary>
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: KsSquadTestMobSyndicate
  id: KsRecoveryTestMob
  components:
  - type: NpcRecovery
    recoveredFraction: 0.5
    maxDuration: 1000

- type: entity
  parent: KsSquadTestMobSyndicate
  id: KsRecoveryTestMobImpatient
  components:
  - type: NpcRecovery
    maxDuration: 1
";

    [Test]
    public async Task TestEndsOnceHealthIsBack()
    {
        var (entManager, mobUid) = await SetUp(RecoveringMob);
        var recoverySystem = entManager.System<NpcRecoverySystem>();

        await Pair.Server.WaitPost(() =>
        {
            SetDamage(entManager, mobUid, 80);
            recoverySystem.StartRecovery(mobUid);
        });

        await Pair.Server.WaitPost(() => recoverySystem.UpdateNow());
        await Pair.RunTicksSync(1);

        await Pair.Server.WaitPost(() =>
        {
            Assert.That(recoverySystem.IsRecovering(mobUid), "still hurt, it should keep waiting");

            SetDamage(entManager, mobUid, 40);
            recoverySystem.UpdateNow();
        });

        await Pair.RunTicksSync(1);

        await Pair.Server.WaitAssertion(() =>
            Assert.That(recoverySystem.IsRecovering(mobUid), Is.False, "with its health back, it should stop waiting"));
    }

    [Test]
    public async Task TestEndsAfterMaxDuration()
    {
        var (entManager, mobUid) = await SetUp(ImpatientMob);
        var recoverySystem = entManager.System<NpcRecoverySystem>();
        var timing = Pair.Server.ResolveDependency<IGameTiming>();
        var seconds = 0f;

        await Pair.Server.WaitPost(() =>
        {
            SetDamage(entManager, mobUid, 80);
            recoverySystem.StartRecovery(mobUid);
            seconds = (float) entManager.GetComponent<NpcRecoveryComponent>(mobUid).MaxDuration.TotalSeconds;
        });

        await Pair.RunTicksSync(1);

        await Pair.Server.WaitPost(() =>
        {
            recoverySystem.UpdateNow();
            Assert.That(recoverySystem.IsRecovering(mobUid), "there should be time left");
        });

        // Its time, and a second for the system's own update to come round after that.
        await Pair.RunTicksSync((int) ((seconds + 1f) * timing.TickRate));

        await Pair.Server.WaitAssertion(() =>
            Assert.That(recoverySystem.IsRecovering(mobUid), Is.False, "out of time, it should stop waiting however hurt it is"));
    }

    /// <summary>
    ///     Having recovered, it does not go back into hiding at the next scratch.
    /// </summary>
    [Test]
    public async Task TestDoesNotRestartOnLaterDamage()
    {
        var (entManager, mobUid) = await SetUp(RecoveringMob);
        var recoverySystem = entManager.System<NpcRecoverySystem>();

        await Pair.Server.WaitPost(() =>
        {
            SetDamage(entManager, mobUid, 80);
            recoverySystem.StartRecovery(mobUid);
            SetDamage(entManager, mobUid, 40);
            recoverySystem.UpdateNow();
        });

        await Pair.RunTicksSync(1);

        await Pair.Server.WaitPost(() =>
        {
            SetDamage(entManager, mobUid, 80);
            recoverySystem.UpdateNow();
        });

        await Pair.RunTicksSync(1);

        await Pair.Server.WaitAssertion(() =>
            Assert.That(recoverySystem.IsRecovering(mobUid), Is.False, "only using meds should start it waiting"));
    }

    [Test]
    public async Task TestGoingDownEndsIt()
    {
        var (entManager, mobUid) = await SetUp(RecoveringMob);
        var recoverySystem = entManager.System<NpcRecoverySystem>();

        await Pair.Server.WaitPost(() =>
        {
            SetDamage(entManager, mobUid, 80);
            recoverySystem.StartRecovery(mobUid);
            entManager.System<MobStateSystem>().ChangeMobState(mobUid, MobState.Critical);
            recoverySystem.UpdateNow();
        });

        await Pair.RunTicksSync(1);

        await Pair.Server.WaitAssertion(() =>
            Assert.That(recoverySystem.IsRecovering(mobUid), Is.False, "one that has gone down is waiting for nothing"));
    }

    /// <summary>
    ///     What makes an NPC do this is having the component: every other NPC that heals carries on as before.
    /// </summary>
    [Test]
    public async Task TestNothingWithoutTheComponent()
    {
        var (entManager, mobUid) = await SetUp(SyndicateMob);
        var recoverySystem = entManager.System<NpcRecoverySystem>();

        await Pair.Server.WaitAssertion(() =>
        {
            recoverySystem.StartRecovery(mobUid);
            Assert.That(recoverySystem.IsRecovering(mobUid), Is.False);
        });
    }

    private void SetDamage(IEntityManager entManager, EntityUid mobUid, float damage)
    {
        var blunt = Pair.Server.ResolveDependency<IPrototypeManager>().Index(BluntDamage);
        entManager.System<DamageableSystem>().SetDamage(mobUid, new DamageSpecifier(blunt, damage));
    }

    private async Task<(IEntityManager EntManager, EntityUid MobUid)> SetUp(string mobPrototype)
    {
        var server = Pair.Server;
        var entManager = server.ResolveDependency<IEntityManager>();
        var tileDefinitionManager = server.ResolveDependency<ITileDefinitionManager>();
        var map = await Pair.CreateTestMap();
        EntityUid mobUid = default;

        await server.WaitPost(() =>
        {
            var gridUid = MakeGrid(entManager, tileDefinitionManager, map.MapId, map.Grid, new Vector2i(-2, -2), new Vector2i(2, 2)).Owner;
            mobUid = SpawnAt(entManager, mobPrototype, gridUid, 0, 0);
        });

        return (entManager, mobUid);
    }
}
