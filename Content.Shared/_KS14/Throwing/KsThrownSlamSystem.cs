using System.Numerics;
using Content.Shared._KS14.ComplexShove;
using Content.Shared._KS14.KsPopup;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Throwing;
using Robust.Shared.Containers;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Throwing;

/// <summary>
///     Slams whoever has a <see cref="KsThrownSlamComponent"/> into the first thing they hit in flight: a shove
///         obstacle (a person, a table, a closet) takes them as it would take a shove, and a wall knocks them down.
///     To use it, throw someone with <see cref="ThrowingSystem"/> and call <see cref="MarkThrown"/> with a
///         <see cref="KsThrownSlamPrototype"/>. Shared, so a throw the client predicts slams on the client too.
/// </summary>
public sealed partial class KsThrownSlamSystem : EntitySystem
{
    [Dependency] private ComplexShoveSystem _complexShoveSystem = default!;
    [Dependency] private DamageableSystem _damageableSystem = default!;
    [Dependency] private INetManager _netManager = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private SharedContainerSystem _containerSystem = default!;
    [Dependency] private SharedPhysicsSystem _physicsSystem = default!;

    /// <summary>
    ///     Marks someone who has just been sent flying, so that the first thing they hit slams them as
    ///         <paramref name="slam"/> says. Call it for the same throw on the client where that is predicted.
    /// </summary>
    /// <param name="source">Who threw them, or null when nobody did.</param>
    public void MarkThrown(EntityUid thrown, ProtoId<KsThrownSlamPrototype> slam, EntityUid? source = null)
    {
        var component = EnsureComp<KsThrownSlamComponent>(thrown);
        component.Slam = slam;
        component.Source = source;
        Dirty(thrown, component);
    }

    [SubscribeLocalEvent]
    private void OnHit(Entity<KsThrownSlamComponent> thrown, ref ThrowDoHitEvent args)
    {
        // Removed already, by an earlier hit this tick: a deferred removal leaves the component in place until the
        // tick ends, and two things hit at once (a window and its grille) would both slam them.
        if (thrown.Comp.LifeStage > ComponentLifeStage.Running)
            return;

        if (args.Target == thrown.Comp.Source || !_prototypeManager.Resolve(thrown.Comp.Slam, out var slam))
            return;

        // Caught by what they hit, as by a closet shutting on them: they're inside it, not slammed against it.
        if (_containerSystem.IsEntityInContainer(thrown))
        {
            RemCompDeferred<KsThrownSlamComponent>(thrown);
            return;
        }

        // With nobody behind the throw they stand in as its source: they are told what happened to them, and
        // everyone else what they saw.
        var source = thrown.Comp.Source ?? thrown.Owner;
        var sourceless = thrown.Comp.Source == null;

        // Read before the slam, which can stop them or put them on a table.
        var momentum = TryComp<PhysicsComponent>(thrown, out var physics) ? physics.LinearVelocity * physics.Mass : Vector2.Zero;

        if (TryComp<KsShoveObstacleComponent>(args.Target, out var obstacle))
        {
            _complexShoveSystem.KnockIntoObstacle(source, thrown, (args.Target, obstacle),
                ForAudience(slam.ObstaclePopups, sourceless));

            // Someone in the way is hit as hard, if they can be hurt; a table isn't.
            if (obstacle.CollateralKnockdownTime != null)
            {
                DealDamage(args.Target, slam.CollateralDamage, thrown.Comp.Source);

                // And is carried on by what hit them.
                if (slam.MomentumTransfer > 0f)
                    _physicsSystem.ApplyLinearImpulse(args.Target, momentum * slam.MomentumTransfer);
            }
        }
        else if (CompOrNull<PhysicsComponent>(args.Target)?.BodyType == BodyType.Static)
        {
            _complexShoveSystem.KnockIntoWall(source, thrown, slam.WallKnockdownTime, slam.WallSound,
                ForAudience(slam.WallPopups, sourceless), ForAudience(slam.WallResistedPopups, sourceless));
        }
        else
        {
            return;
        }

        // Only the first thing they slam into counts.
        RemCompDeferred<KsThrownSlamComponent>(thrown);
        DealDamage(thrown, slam.ImpactDamage, thrown.Comp.Source);

        // Unless what they hit took them inside, they stop where they hit it.
        if (slam.StopsMomentum && !_containerSystem.IsEntityInContainer(thrown))
        {
            _physicsSystem.SetLinearVelocity(thrown, Vector2.Zero);
            _physicsSystem.SetAngularVelocity(thrown, 0f);
        }
    }

    /// <summary>
    ///     The popups as shown when the thrown stands in for a missing source: theirs is the target's message.
    /// </summary>
    private static KsAudiencePopups ForAudience(KsAudiencePopups popups, bool sourceless)
    {
        return sourceless ? new KsAudiencePopups(popups.Target, null, popups.Others) : popups;
    }

    /// <summary>
    ///     Damage from the slam, dealt by the server only, so health doesn't flicker on a predicting client.
    /// </summary>
    private void DealDamage(EntityUid uid, DamageSpecifier? damage, EntityUid? origin)
    {
        if (_netManager.IsClient || damage == null || damage.Empty)
            return;

        _damageableSystem.ChangeDamage(uid, damage, origin: origin);
    }

    [SubscribeLocalEvent]
    private void OnLand(Entity<KsThrownSlamComponent> thrown, ref LandEvent args)
    {
        RemCompDeferred<KsThrownSlamComponent>(thrown);
    }

    [SubscribeLocalEvent]
    private void OnStop(Entity<KsThrownSlamComponent> thrown, ref StopThrowEvent args)
    {
        RemCompDeferred<KsThrownSlamComponent>(thrown);
    }
}
