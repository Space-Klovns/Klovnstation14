using System.Collections.Generic;
using System.Numerics;
using Content.Client._KS14.Alligators;
using Content.IntegrationTests.Fixtures;
using Content.Server._KS14.Alligators;
using Content.Server.Gravity;
using Content.Server.NPC.HTN;
using Content.Shared.Buckle.Components;
using Content.Shared.CombatMode;
using Content.Shared.Gravity;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Stunnable;
using Content.Shared.Weapons.Melee;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._KS14.Alligators;

/// <summary>
///     A death roll's victim is drawn along the alligator and rolled over, whatever way they face themselves.
/// </summary>
[TestFixture]
[TestOf(typeof(KsDeathRollVictimVisualsSystem))]
public sealed class KsDeathRollVictimVisualsTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private static readonly EntProtoId AlligatorPrototype = "KsMobSpaceAlligator";
    private static readonly EntProtoId HumanPrototype = "MobHuman";

    [Test]
    public async Task VictimLiesAlongAlligatorAndRollsUntilReleased()
    {
        var map = await Pair.CreateTestMap();
        EntityUid alligatorUid = default;
        EntityUid victimUid = default;
        var rollDuration = TimeSpan.Zero;

        await Server.WaitAssertion(() =>
        {
            var gravity = SEntMan.EnsureComponent<GravityComponent>(map.Grid.Owner);
            SEntMan.System<GravitySystem>().EnableGravity(map.Grid.Owner, gravity);
            alligatorUid = SSpawnAtPosition(AlligatorPrototype, map.GridCoords.Offset(new Vector2(0.2f, 0.2f)));
            // Off to one side, so the alligator has to turn to take hold.
            victimUid = SSpawnAtPosition(HumanPrototype, map.GridCoords.Offset(new Vector2(0.8f, 0.5f)));

            SEntMan.RemoveComponent<HTNComponent>(alligatorUid);
            SEntMan.System<SharedCombatModeSystem>().SetInCombatMode(alligatorUid, true);
            rollDuration = SComp<KsAlligatorComponent>(alligatorUid).RollDuration;

            Assert.That(SEntMan.System<SharedStunSystem>().TryKnockdown(victimUid, rollDuration), Is.True);
            Assert.That(SEntMan.System<SharedMeleeWeaponSystem>().AttemptLightAttack(
                alligatorUid, alligatorUid, SComp<MeleeWeaponComponent>(alligatorUid), victimUid), Is.True);
            Assert.That(SComp<BuckleComponent>(victimUid).BuckledTo, Is.EqualTo(alligatorUid));

            // The victim turning, as combat mode makes them, must not show.
            SEntMan.System<SharedTransformSystem>().SetLocalRotation(victimUid, Angle.FromDegrees(135));
        });

        await Pair.RunTicksSync(10);

        var clientVictimUid = ToClientUid(victimUid);
        var clientAlligatorUid = ToClientUid(alligatorUid);
        var facings = new HashSet<Direction>();
        var samples = (int)(rollDuration.TotalSeconds * Server.Timing.TickRate / 2);
        for (var sample = 0; sample < samples; sample += 5)
        {
            await Pair.RunTicksSync(5);
            await Client.WaitAssertion(() =>
            {
                var sprite = CComp<SpriteComponent>(clientVictimUid);
                Assert.That(sprite.EnableDirectionOverride, Is.True);
                facings.Add(sprite.DirectionOverride);

                // Head away from the alligator: an upright sprite's head is to the north, the opposite of angle zero.
                var alligatorFacing = CEntMan.System<SharedTransformSystem>().GetWorldRotation(clientAlligatorUid) +
                    Client.ResolveDependency<IEyeManager>().CurrentEye.Rotation;
                var headDirection = sprite.Rotation + Math.PI;
                Assert.That(Math.Abs(Angle.ShortestDistance(headDirection, alligatorFacing).Theta), Is.LessThan(0.01));
            });
        }

        Assert.That(facings, Has.Count.GreaterThan(2), "The victim was not rolled through its facings.");

        await Server.WaitPost(() => SEntMan.System<MobStateSystem>().ChangeMobState(alligatorUid, MobState.Dead));
        await Pair.RunTicksSync(10);
        await Client.WaitAssertion(() =>
            Assert.That(CComp<SpriteComponent>(clientVictimUid).EnableDirectionOverride, Is.False));
    }
}
