using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.IntegrationTests.Pair;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.Mobs.Components;
using Content.Shared._KS14.Mobs.Systems;
using Content.Shared.ActionBlocker;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Server.Gravity;
using Content.Shared.FixedPoint;
using Content.Shared.Gravity;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Stunnable;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Mobs;

/// <summary>
///     Soft crit: the first stretch of crit, where a mob is floored but can still crawl and speak,
///         and how it follows the mob's damage and the cvar.
/// </summary>
[TestFixture]
[TestOf(typeof(KsSoftCritSystem))]
public sealed class KsSoftCritTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    // The window is the test's own, so a retune of the real one doesn't move it. The thresholds are the
    //      species' and are read back from the mob.
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: MobHuman
  id: KsTestSoftCritMob
  name: test soft crit mob
  components:
  - type: KsSoftCrit
    hardCritFraction: 0.5
";

    private readonly EntProtoId _mobProto = "KsTestSoftCritMob";
    private static readonly ProtoId<DamageTypePrototype> BluntDamage = "Blunt";

    private EntityUid _mobUid;
    private EntityUid _gridUid;

    /// <summary>
    ///     The damage at which soft crit starts, and how long it lasts, as the mob itself has them.
    /// </summary>
    private FixedPoint2 _critThreshold;
    private FixedPoint2 _softCritLength;

    private async Task SpawnMob()
    {
        var testMap = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            // A station has gravity, and without it nothing can be knocked down.
            _gridUid = testMap.Grid.Owner;
            var gravityComponent = SEntMan.EnsureComponent<GravityComponent>(_gridUid);
            SEntMan.System<GravitySystem>().EnableGravity(_gridUid, gravityComponent);

            _mobUid = SEntMan.SpawnAtPosition(_mobProto, testMap.GridCoords);

            var thresholdSystem = SEntMan.System<MobThresholdSystem>();
            _critThreshold = thresholdSystem.GetThresholdForState(_mobUid, MobState.Critical);
            var deadThreshold = thresholdSystem.GetThresholdForState(_mobUid, MobState.Dead);
            var softCritComponent = SEntMan.GetComponent<KsSoftCritComponent>(_mobUid);
            _softCritLength = (deadThreshold - _critThreshold) * softCritComponent.HardCritFraction;

            Assert.That(_softCritLength, Is.GreaterThan(FixedPoint2.Zero),
                "The test mob has no room for soft crit between its crit and dead thresholds.");
        });
    }

    private async Task ChangeDamage(FixedPoint2 amount)
    {
        await Server.WaitAssertion(() =>
        {
            var damage = new DamageSpecifier(SProtoMan.Index(BluntDamage), amount);
            SEntMan.System<DamageableSystem>().TryChangeDamage(_mobUid, damage, ignoreResistances: true);
        });
        await Server.WaitRunTicks(1);
    }

    private void AssertSoftCrit(string context)
    {
        var actionBlockerSystem = SEntMan.System<ActionBlockerSystem>();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(SEntMan.System<MobStateSystem>().IsCritical(_mobUid), Is.True,
                $"{context}: the mob is not critical at all.");
            Assert.That(SEntMan.System<KsSoftCritSystem>().IsSoftCrit(_mobUid), Is.True,
                $"{context}: the mob is not in soft crit.");
            Assert.That(SEntMan.HasComponent<KnockedDownComponent>(_mobUid), Is.True,
                $"{context}: a soft crit mob was left standing.");
            Assert.That(actionBlockerSystem.CanMove(_mobUid), Is.True,
                $"{context}: a soft crit mob can't crawl.");
            Assert.That(actionBlockerSystem.CanSpeak(_mobUid), Is.True,
                $"{context}: a soft crit mob can't speak.");
            Assert.That(actionBlockerSystem.CanInteract(_mobUid, null), Is.False,
                $"{context}: a soft crit mob can still use its hands.");
        }
    }

    private void AssertHardCrit(string context)
    {
        var actionBlockerSystem = SEntMan.System<ActionBlockerSystem>();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(SEntMan.System<MobStateSystem>().IsCritical(_mobUid), Is.True,
                $"{context}: the mob is not critical at all.");
            Assert.That(SEntMan.System<KsSoftCritSystem>().IsSoftCrit(_mobUid), Is.False,
                $"{context}: the mob is still in soft crit.");
            Assert.That(actionBlockerSystem.CanMove(_mobUid), Is.False,
                $"{context}: a hard crit mob can still move.");
            Assert.That(actionBlockerSystem.CanSpeak(_mobUid), Is.False,
                $"{context}: a hard crit mob can still speak.");
        }
    }

    [Test]
    public async Task TestSoftCritFollowsDamage()
    {
        await SpawnMob();

        // Halfway into soft crit.
        await ChangeDamage(_critThreshold + _softCritLength * 0.5f);
        await Server.WaitAssertion(() => AssertSoftCrit("Just past the crit threshold"));

        // Past the end of the window, still short of dead.
        await ChangeDamage(_softCritLength);
        await Server.WaitAssertion(() => AssertHardCrit("Past the soft crit window"));

        // Healed back into it.
        await ChangeDamage(-_softCritLength);
        await Server.WaitAssertion(() => AssertSoftCrit("Healed back from hard crit"));

        // Healed out of crit entirely: the knockdown hands back to the normal stand-up.
        await ChangeDamage(-(_critThreshold + _softCritLength));
        await Server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(SEntMan.System<MobStateSystem>().IsAlive(_mobUid), Is.True,
                    "The mob did not recover from crit after being healed.");
                Assert.That(SEntMan.System<KsSoftCritSystem>().IsSoftCrit(_mobUid), Is.False,
                    "A healthy mob is still in soft crit.");

                if (SEntMan.TryGetComponent<KnockedDownComponent>(_mobUid, out var knockedDownComponent))
                {
                    Assert.That(knockedDownComponent.AutoStand, Is.True,
                        "A mob healed out of soft crit will never get back up on its own.");
                }
            }
        });
    }

    [Test]
    public async Task TestDisablingTheCVarEndsSoftCrit()
    {
        await SpawnMob();

        await ChangeDamage(_critThreshold + _softCritLength * 0.5f);
        await Server.WaitAssertion(() => AssertSoftCrit("Before disabling soft crit"));

        await OverrideCVar(Side.Server, KsCCVars.SoftCritEnabled, false);
        await Server.WaitRunTicks(1);
        await Server.WaitAssertion(() => AssertHardCrit("After disabling soft crit"));

        await OverrideCVar(Side.Server, KsCCVars.SoftCritEnabled, true);
        await Server.WaitRunTicks(1);
        await Server.WaitAssertion(() => AssertSoftCrit("After enabling soft crit again"));
    }

    [Test]
    public async Task TestGravityReturningKnocksASoftCritMobBackDown()
    {
        await SpawnMob();

        await ChangeDamage(_critThreshold + _softCritLength * 0.5f);
        await Server.WaitAssertion(() => AssertSoftCrit("Before losing gravity"));

        // No generator on the grid, so a refresh turns gravity off. Going weightless ends any knockdown.
        await Server.WaitPost(() => SEntMan.System<GravitySystem>().RefreshGravity(_gridUid));
        await Server.WaitRunTicks(2);
        await Server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(SEntMan.System<SharedGravitySystem>().IsWeightless(_mobUid), Is.True,
                    "Turning the grid's gravity off did not make the mob weightless, so this test checks nothing.");
                Assert.That(SEntMan.HasComponent<KnockedDownComponent>(_mobUid), Is.False,
                    "Going weightless did not end the knockdown, so this test checks nothing.");
            }
        });

        await Server.WaitPost(() => SEntMan.System<GravitySystem>().EnableGravity(_gridUid));
        await Server.WaitRunTicks(2);
        await Server.WaitAssertion(() => AssertSoftCrit("After gravity came back"));
    }
}
