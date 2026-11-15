using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Pair;
using Content.Server._KS14.Alligators;
using Content.Server.Gravity;
using Content.Server.NPC.HTN;
using Content.Shared._KS14.Klovnmed;
using Content.Shared.ActionBlocker;
using Content.Shared.Body;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.CombatMode;
using Content.Shared.Gravity;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Standing;
using Content.Shared.Stunnable;
using Content.Shared.Weapons.Melee;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Alligators;

[TestFixture]
[TestOf(typeof(KsAlligatorSystem))]
public sealed class KsAlligatorTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private const string AlligatorPrototype = "KsMobSpaceAlligator";
    private const string HumanPrototype = "MobHuman";
    private static readonly ProtoId<OrganCategoryPrototype> LeftLeg = "LegLeft";
    private static readonly ProtoId<OrganCategoryPrototype> RightLeg = "LegRight";

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
    public async Task RepeatedBitesKnockDownAndDeathRollRemovesOneLeg()
    {
        await SpawnFight();
        var standing = SEntMan.System<StandingStateSystem>();

        for (var bite = 0; bite < 2; bite++)
        {
            await Bite();
            await Server.WaitAssertion(() =>
            {
                Assert.That(standing.IsDown(_victimUid), Is.False);
                Assert.That(SComp<BuckleComponent>(_victimUid).BuckledTo, Is.Null);
                Assert.That(CountLegs(), Is.EqualTo(2));
            });
            await Advance(_biteInterval);
        }

        await Bite();
        await Server.WaitAssertion(() =>
        {
            Assert.That(standing.IsDown(_victimUid), Is.True);
            Assert.That(SComp<BuckleComponent>(_victimUid).BuckledTo, Is.Null);
        });
        await Advance(_biteInterval);

        await Bite();
        var buckle = SComp<BuckleComponent>(_victimUid);
        await Advance(buckle.Delay);
        await Server.WaitAssertion(() =>
        {
            Assert.That(buckle.BuckledTo, Is.EqualTo(_alligatorUid));
            Assert.That(SEntMan.System<SharedBuckleSystem>().TryUnbuckle(_victimUid, _victimUid, popup: false), Is.False);
            Assert.That(SEntMan.System<ActionBlockerSystem>().CanMove(_alligatorUid), Is.False);
            Assert.That(CountLegs(), Is.EqualTo(2));
        });

        await Advance(_rollDuration);
        await Server.WaitAssertion(() =>
        {
            Assert.That(buckle.BuckledTo, Is.Null);
            Assert.That(CountLegs(), Is.EqualTo(1));
            Assert.That(SEntMan.System<ActionBlockerSystem>().CanMove(_alligatorUid), Is.True);
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
