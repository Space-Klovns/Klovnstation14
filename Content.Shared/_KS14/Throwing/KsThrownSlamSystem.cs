using System.Numerics;
using Content.Shared._KS14.ComplexShove;
using Content.Shared._KS14.KsPopup;
using Content.Shared.Administration.Logs;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Database;
using Content.Shared.FixedPoint;
using Content.Shared.Throwing;
using Robust.Shared.Containers;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

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
    [Dependency] private EntityQuery<KsShoveObstacleComponent> _obstacleQuery = default!;
    [Dependency] private EntityQuery<PhysicsComponent> _physicsQuery = default!;
    [Dependency] private DamageableSystem _damageableSystem = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private INetManager _netManager = default!;
    [Dependency] private ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private SharedContainerSystem _containerSystem = default!;
    [Dependency] private SharedPhysicsSystem _physicsSystem = default!;

    /// <summary>
    ///     How long after a client announced a predicted slam it stays quiet about another of the same entity.
    /// </summary>
    private static readonly TimeSpan RepeatedSlamWindow = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     Client only: when each thrown entity's predicted slam was last announced. Entries past
    ///         <see cref="RepeatedSlamWindow"/> are dropped on the next slam.
    /// </summary>
    private readonly Dictionary<EntityUid, TimeSpan> _slamAnnouncements = new();

    private readonly List<EntityUid> _expiredSlamAnnouncements = new();

    /// <summary>
    ///     Marks someone who has just been sent flying, so that the first thing they hit slams them as
    ///         <paramref name="slam"/> says. Call it for the same throw on the client where that is predicted.
    /// </summary>
    /// <param name="source">Who threw them, or null when nobody did.</param>
    public void MarkThrown(EntityUid thrown, ProtoId<KsThrownSlamPrototype> slam, EntityUid? source = null)
    {
        var thrownSlamComponent = EnsureComp<KsThrownSlamComponent>(thrown);
        thrownSlamComponent.Slam = slam;
        thrownSlamComponent.Source = source;
        Dirty(thrown, thrownSlamComponent);
    }

    [SubscribeLocalEvent]
    private void OnHit(Entity<KsThrownSlamComponent> thrown, ref ThrowDoHitEvent args)
    {
        // Removed already, by an earlier hit this tick: a deferred removal leaves the component in place until the
        // tick ends, and two things hit at once (a window and its grille) would both slam them.
        if (thrown.Comp.LifeStage > ComponentLifeStage.Running)
            return;

        if (args.Target == thrown.Comp.Source || !ProtoMan.Resolve(thrown.Comp.Slam, out var slam))
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

        // A client that predicts the same slam twice says it once.
        var announce = ShouldAnnounce(thrown);

        // Read before the slam, which can stop them or put them on a table.
        var momentum = _physicsQuery.TryComp(thrown, out var thrownPhysicsComponent)
            ? thrownPhysicsComponent.LinearVelocity * thrownPhysicsComponent.Mass
            : Vector2.Zero;

        var collateralDamageDealt = FixedPoint2.Zero;
        if (_obstacleQuery.TryComp(args.Target, out var obstacleComponent))
        {
            _complexShoveSystem.KnockIntoObstacle(source, thrown, (args.Target, obstacleComponent),
                popups: ForAudience(slam.ObstaclePopups, sourceless), announce: announce);

            // Someone in the way is hit as hard, if they can be hurt; a table isn't.
            if (obstacleComponent.CollateralKnockdownTime != null)
            {
                collateralDamageDealt = DealDamage(args.Target, slam.CollateralDamage, thrown.Comp.Source);

                // And is carried on by what hit them.
                if (slam.MomentumTransfer > 0f)
                    _physicsSystem.ApplyLinearImpulse(args.Target, momentum * slam.MomentumTransfer);
            }
        }
        else if (_physicsQuery.CompOrNull(args.Target)?.BodyType == BodyType.Static)
        {
            _complexShoveSystem.KnockIntoWall(source, thrown, slam.WallKnockdownTime, slam.WallSound,
                ForAudience(slam.WallPopups, sourceless), ForAudience(slam.WallResistedPopups, sourceless),
                downedPopups: ForAudience(slam.WallDownedPopups, sourceless), announce: announce);
        }
        else
        {
            return;
        }

        // Only the first thing they slam into counts.
        RemCompDeferred<KsThrownSlamComponent>(thrown);
        var impactDamageDealt = DealDamage(thrown, slam.ImpactDamage, thrown.Comp.Source);

        // That they hit it is logged by the throw, and that it knocked them over by the shove system: what is left to
        // say is what it cost them.
        if (impactDamageDealt > FixedPoint2.Zero || collateralDamageDealt > FixedPoint2.Zero)
        {
            if (thrown.Comp.Source is { } thrower)
            {
                _adminLogger.Add(LogType.ThrowHit, LogImpact.Medium,
                    $"{ToPrettyString(thrown):target}, thrown by {ToPrettyString(thrower):user}, took {impactDamageDealt:damage} damage slamming into {ToPrettyString(args.Target):obstacle}, which took {collateralDamageDealt:collateral} damage");
            }
            else
            {
                _adminLogger.Add(LogType.ThrowHit, LogImpact.Medium,
                    $"{ToPrettyString(thrown):target}, thrown by nobody, took {impactDamageDealt:damage} damage slamming into {ToPrettyString(args.Target):obstacle}, which took {collateralDamageDealt:collateral} damage");
            }
        }

        // Unless what they hit took them inside, they stop where they hit it.
        if (slam.StopsMomentum && !_containerSystem.IsEntityInContainer(thrown))
        {
            _physicsSystem.SetLinearVelocity(thrown, Vector2.Zero);
            _physicsSystem.SetAngularVelocity(thrown, 0f);
        }
    }

    /// <summary>
    ///     Whether this slam of <paramref name="thrown"/> is to be heard and seen. On the server, always. A client
    ///         predicts the hit on its own tick, and each state before the server confirms it brings the marker back,
    ///         so that it can predict the same slam again a tick or two later, against something moving above all:
    ///         it then announces only the first within <see cref="RepeatedSlamWindow"/>.
    /// </summary>
    private bool ShouldAnnounce(EntityUid thrown)
    {
        if (_netManager.IsServer)
            return true;

        // Nothing is shown or played on a repeated prediction pass anyway, and it mustn't count as the first.
        if (!_gameTiming.IsFirstTimePredicted)
            return false;

        var curTime = _gameTiming.CurTime;
        _expiredSlamAnnouncements.Clear();
        foreach (var (announcedUid, announcedAt) in _slamAnnouncements)
        {
            if (curTime - announcedAt >= RepeatedSlamWindow || curTime < announcedAt)
                _expiredSlamAnnouncements.Add(announcedUid);
        }

        foreach (var expiredUid in _expiredSlamAnnouncements)
        {
            _slamAnnouncements.Remove(expiredUid);
        }

        return _slamAnnouncements.TryAdd(thrown, curTime);
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
    /// <returns>How much was dealt in all: nothing on a client.</returns>
    private FixedPoint2 DealDamage(EntityUid uid, DamageSpecifier? damage, EntityUid? origin)
    {
        if (_netManager.IsClient || damage == null || damage.Empty)
            return FixedPoint2.Zero;

        return _damageableSystem.ChangeDamage(uid, damage, origin: origin).GetTotal();
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
