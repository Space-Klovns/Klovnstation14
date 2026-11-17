using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Pair;
using Content.Server._KS14.Alligators;
using Content.Server.Gravity;
using Content.Server.NPC.HTN;
using Content.Shared._KS14.Alligators;
using Content.Shared._KS14.Klovnmed;
using Content.Shared.ActionBlocker;
using Content.Shared.Body;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.CombatMode;
using Content.Shared.Destructible;
using Content.Shared.Gravity;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Standing;
using Content.Shared.Stunnable;
using Content.Shared.Weapons.Melee;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Alligators;

[TestFixture]
[TestOf(typeof(KsAlligatorSystem))]
public sealed class KsAlligatorTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private static readonly EntProtoId AlligatorPrototype = "KsMobSpaceAlligator";
    private static readonly EntProtoId HumanPrototype = "MobHuman";
    private static readonly ProtoId<OrganCategoryPrototype> LeftLeg = "LegLeft";
    private static readonly ProtoId<OrganCategoryPrototype> RightLeg = "LegRight";

    /// <summary>
    ///     Far more bites than any tuning should need to floor a fresh victim and latch onto them.
    /// </summary>
    private const int MaxBitesToLatch = 40;

    private EntityUid _alligatorUid;
    private EntityUid _victimUid;
    private EntityUid _rescuerUid;
    private TimeSpan _biteInterval;
    private TimeSpan _rollDuration;

    private async Task SpawnFight()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var gravity = SEntMan.EnsureComponent<GravityComponent>(map.Grid.Owner);
            SEntMan.System<GravitySystem>().EnableGravity(map.Grid.Owner, gravity);
            _alligatorUid = SSpawnAtPosition(AlligatorPrototype, map.GridCoords.Offset(new Vector2(0.2f, 0.2f)));
            _victimUid = SSpawnAtPosition(HumanPrototype, map.GridCoords.Offset(new Vector2(0.8f, 0.2f)));
            _rescuerUid = SSpawnAtPosition(HumanPrototype, map.GridCoords.Offset(new Vector2(0.2f, 0.8f)));

            // Keep the timing deterministic while exercising the same attack entry point the AI uses.
            SEntMan.RemoveComponent<HTNComponent>(_alligatorUid);
            SEntMan.System<SharedCombatModeSystem>().SetInCombatMode(_alligatorUid, true);
            _biteInterval = TimeSpan.FromSeconds(1 / SComp<MeleeWeaponComponent>(_alligatorUid).AttackRate);
            _rollDuration = SComp<KsAlligatorComponent>(_alligatorUid).RollDuration;
            Assert.That(CountLegs(), Is.EqualTo(2));
        });
    }

    [Test]
    public async Task BitesFloorVictimThenDeathRollSwallowsOneLeg()
    {
        await SpawnFight();
        var standing = SEntMan.System<StandingStateSystem>();
        var buckle = SComp<BuckleComponent>(_victimUid);

        // A bite only latches onto someone already on the floor, and a fresh victim takes several to put there.
        var bites = 0;
        while (buckle.BuckledTo == null)
        {
            Assert.That(++bites, Is.LessThanOrEqualTo(MaxBitesToLatch), "Bites never floored and latched onto the victim.");
            var wasDown = false;
            await Server.WaitPost(() => wasDown = standing.IsDown(_victimUid));
            await Bite();
            await Advance(_biteInterval);
            if (!wasDown && bites == 1)
                Assert.That(buckle.BuckledTo, Is.Null, "The first bite latched onto a standing victim.");
        }

        Assert.That(bites, Is.GreaterThan(1));
        EntityUid? leg = null;
        await Server.WaitAssertion(() =>
        {
            Assert.That(buckle.BuckledTo, Is.EqualTo(_alligatorUid));
            Assert.That(SEntMan.System<SharedBuckleSystem>().TryUnbuckle(_victimUid, _victimUid, popup: false), Is.False);
            Assert.That(SEntMan.System<ActionBlockerSystem>().CanMove(_alligatorUid), Is.False);
            Assert.That(SEntMan.HasComponent<KsDeathRollVictimComponent>(_victimUid), Is.True);
            Assert.That(CountLegs(), Is.EqualTo(2));
            leg = SComp<KsAlligatorComponent>(_alligatorUid).Leg;
            Assert.That(leg, Is.Not.Null);
        });

        await Advance(_rollDuration);
        var containers = SEntMan.System<SharedContainerSystem>();
        await Server.WaitAssertion(() =>
        {
            Assert.That(buckle.BuckledTo, Is.Null);
            Assert.That(CountLegs(), Is.EqualTo(1));
            Assert.That(SEntMan.System<ActionBlockerSystem>().CanMove(_alligatorUid), Is.True);
            Assert.That(SEntMan.HasComponent<KsDeathRollVictimComponent>(_victimUid), Is.False);

            // The leg is inside the alligator, and comes back out when it is butchered.
            Assert.That(containers.TryGetContainingContainer(leg.Value, out var stomach), Is.True);
            Assert.That(stomach.Owner, Is.EqualTo(_alligatorUid));
            Assert.That(SEntMan.System<SharedDestructibleSystem>().DestroyEntity(_alligatorUid), Is.True);
        });

        await Pair.RunTicksSync(5);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.EntityExists(_alligatorUid), Is.False);
            Assert.That(SEntMan.EntityExists(leg.Value), Is.True);
            Assert.That(containers.IsEntityInContainer(leg.Value), Is.False);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task InterruptedRollReleasesVictimWithoutSeveringLeg(bool killAlligator)
    {
        await SpawnFight();
        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.System<SharedStunSystem>().TryKnockdown(_victimUid, _rollDuration), Is.True));
        await Bite();
        var buckle = SComp<BuckleComponent>(_victimUid);
        await Advance(buckle.Delay);

        await Server.WaitAssertion(() =>
        {
            Assert.That(buckle.BuckledTo, Is.EqualTo(_alligatorUid));
            if (killAlligator)
                SEntMan.System<MobStateSystem>().ChangeMobState(_alligatorUid, MobState.Dead);
            else
                Assert.That(SEntMan.System<SharedBuckleSystem>().TryUnbuckle(_victimUid, _rescuerUid, popup: false), Is.True);
            Assert.That(buckle.BuckledTo, Is.Null);
        });

        await Advance(_rollDuration);
        await Server.WaitAssertion(() => Assert.That(CountLegs(), Is.EqualTo(2)));
    }

    private Task Bite()
        => Server.WaitAssertion(() =>
            Assert.That(SEntMan.System<SharedMeleeWeaponSystem>().AttemptLightAttack(
                _alligatorUid, _alligatorUid, SComp<MeleeWeaponComponent>(_alligatorUid), _victimUid), Is.True));

    private Task Advance(TimeSpan duration)
        => Pair.RunTicksSync((int)Math.Ceiling(duration.TotalSeconds * Server.Timing.TickRate) + 2);

    private int CountLegs()
    {
        var body = SEntMan.System<BodyHierarchySystem>();
        return (body.TryGetOrgan(_victimUid, LeftLeg, out _) ? 1 : 0) +
            (body.TryGetOrgan(_victimUid, RightLeg, out _) ? 1 : 0);
    }
}
