using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using Content.Shared._KS14.KsPopup;
using Content.Shared._KS14.PopupLocalization;
using Content.Shared.Administration.Logs;
using Content.Shared.Climbing.Components;
using Content.Shared.Climbing.Systems;
using Content.Shared.CombatMode;
using Content.Shared.Damage.Components;
using Content.Shared.Database;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.IdentityManagement;
using Content.Shared.Inventory.VirtualItem;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Standing;
using Content.Shared.Stunnable;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Physics.Systems;
using DependencyAttribute = Robust.Shared.IoC.DependencyAttribute;

namespace Content.Shared._KS14.ComplexShove;

/// <summary>
///     tgstation's shove. Resolved in this order:
///     <list type="number">
///         <item>Someone already down is kicked onto their side and paralyzed, once until they stand up.</item>
///         <item>Someone shoved into a <see cref="KsShoveObstacleComponent"/> (a table, another person) is knocked over by it.</item>
///         <item>Someone shoved against a wall or anything else solid is knocked down.</item>
///         <item>Otherwise they are pushed away and staggered.</item>
///     </list>
///     Shoves do no stamina damage.
/// </summary>
public sealed partial class ComplexShoveSystem : EntitySystem
{
    [Dependency] private ClimbSystem _climbSystem = default!;
    [Dependency] private EntityLookupSystem _entityLookupSystem = default!;
    [Dependency] private ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private KsPopupSystem _ksPopupSystem = default!;
    [Dependency] private MobStateSystem _mobStateSystem = default!;
    [Dependency] private MovementModStatusSystem _movementModStatusSystem = default!;
    [Dependency] private RayCastSystem _rayCastSystem = default!;
    [Dependency] private SharedAudioSystem _audioSystem = default!;
    [Dependency] private SharedHandsSystem _handsSystem = default!;
    [Dependency] private SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private SharedStunSystem _stunSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private StandingStateSystem _standingStateSystem = default!;

    private readonly HashSet<Entity<KsShoveObstacleComponent>> _obstacles = new();

    [SubscribeLocalEvent]
    private void OnStood(EntityUid uid, KsShoveParalyzedComponent component, StoodEvent args)
    {
        RemCompDeferred<KsShoveParalyzedComponent>(uid);
    }

    [SubscribeLocalEvent]
    private void OnDisarmed(Entity<StandingStateComponent> target, ref DisarmedEvent args)
    {
        if (args.Handled || !TryComp<ComplexShoveComponent>(args.Source, out var shove) ||
            !TryGetShoveDirection(args.Source, target, out var direction, out var targetPosition))
        {
            return;
        }

        args.Handled = true;
        var shover = (args.Source, shove);
        var outcome = ResolveShove(shover, target, direction.Value, targetPosition.Value, out var obstacle, ref args);

        var shovedEvent = new KsShovedEvent(args.Source, target, outcome, obstacle);
        RaiseLocalEvent(target, ref shovedEvent);
    }

    private KsShoveOutcome ResolveShove(Entity<ComplexShoveComponent> shover, Entity<StandingStateComponent> target,
        Vector2 direction, MapCoordinates targetPosition, out EntityUid? obstacle, ref DisarmedEvent args)
    {
        obstacle = null;

        // Someone already out of the fight, in crit or stamina crit, is only pushed: nothing to knock down, kick or pin.
        if (_mobStateSystem.IsIncapacitated(target) || CompOrNull<StaminaComponent>(target)?.Critical == true)
        {
            Push(shover, target, direction, targetDown: true);
            args.PopupPrefix = "disarm-action-shove-";
            return KsShoveOutcome.Pushed;
        }

        var before = new KsBeforeShoveEvent(shover, target);
        RaiseLocalEvent(target, ref before);
        if (before.Handled)
        {
            args.KsPopupHandled = true;
            return KsShoveOutcome.Handled;
        }

        // Someone already down can't be knocked down again: they're kicked over, then disarmed against a wall, then only
        // pushed.
        if (_standingStateSystem.IsDown(target.AsNullable()))
        {
            var canDisarm = CanDisarmAgainst(shover, target, targetPosition, direction);
            var downOutcome = KickWhileDown(shover, target, canDisarm);
            if (downOutcome != KsShoveOutcome.Pushed)
            {
                args.KsPopupHandled = true;
                return downOutcome;
            }

            // No stagger either: that waits until they've stood up.
            Push(shover, target, direction, targetDown: true);
            args.PopupPrefix = "disarm-action-shove-";
            return KsShoveOutcome.Pushed;
        }

        var probe = targetPosition.Offset(direction * shover.Comp.ProbeDistance);
        if (TryGetObstacle(shover, target, probe, out var found))
        {
            obstacle = found.Value.Owner;
            args.KsPopupHandled = true;
            KnockIntoObstacle(shover, target, found.Value);
            return KsShoveOutcome.Obstacle;
        }

        if (IsBlocked(shover, targetPosition, direction))
        {
            args.KsPopupHandled = true;
            KnockIntoWall(shover, target, shover.Comp.BlockedKnockdownTime, shover.Comp.BlockedSound, "ks-shove-wall-");
            return KsShoveOutcome.Blocked;
        }

        Push(shover, target, direction, targetDown: false);
        _movementModStatusSystem.TryUpdateMovementSpeedModDuration(target,
            shover.Comp.StaggerEffect,
            shover.Comp.StaggerTime,
            shover.Comp.StaggerSpeedModifier);

        args.PopupPrefix = "disarm-action-shove-";
        return KsShoveOutcome.Pushed;
    }

    /// <summary>
    ///     Whether what is behind <paramref name="target"/> lets a shove knock things out of their hands: a wall, by
    ///         <see cref="ComplexShoveComponent.DisarmAgainstWalls"/>, or an obstacle with
    ///         <see cref="KsShoveObstacleComponent.DisarmsAgainst"/>. <see cref="KsShoveDisarmCheckEvent"/> has the last word.
    /// </summary>
    public bool CanDisarmAgainst(Entity<ComplexShoveComponent> shover, EntityUid target, MapCoordinates targetPosition,
        Vector2 direction)
    {
        EntityUid? surface = null;
        var allowed = false;

        var probe = targetPosition.Offset(direction * shover.Comp.ProbeDistance);
        if (TryGetObstacle(shover, target, probe, out var obstacle))
        {
            surface = obstacle.Value.Owner;
            allowed = obstacle.Value.Comp.DisarmsAgainst;
        }
        else if (IsBlocked(shover, targetPosition, direction))
        {
            allowed = shover.Comp.DisarmAgainstWalls;
        }

        var check = new KsShoveDisarmCheckEvent(shover, target, surface, allowed);
        RaiseLocalEvent(target, ref check);
        return check.Allowed;
    }

    /// <summary>
    ///     A shove on someone lying down: the first kicks them onto their side and paralyzes them, the next, against a
    ///         wall, makes them drop everything they hold. Neither repeats until they have stood up again.
    /// </summary>
    /// <param name="canDisarm">Whether this shove pins them against something that knocks things out of their hands.</param>
    /// <returns><see cref="KsShoveOutcome.Pushed"/> when there is nothing left to do but push them.</returns>
    private KsShoveOutcome KickWhileDown(Entity<ComplexShoveComponent> shover, EntityUid target, bool canDisarm)
    {
        if (!TryComp<KsShoveParalyzedComponent>(target, out var paralyzed))
        {
            // Stunned by something else: a kick wouldn't add anything, and mustn't extend it.
            if (HasComp<StunnedComponent>(target) ||
                !_stunSystem.TryUpdateParalyzeDuration(target, shover.Comp.ChainParalyzeTime, drop: false))
            {
                return KsShoveOutcome.Pushed;
            }

            EnsureComp<KsShoveParalyzedComponent>(target);
            PopupShove(shover, target, "ks-shove-paralyze-");
            _adminLogger.Add(LogType.DisarmedKnockdown, LogImpact.Medium,
                $"{ToPrettyString(shover):user} kicked {ToPrettyString(target):target} onto their side");
            return KsShoveOutcome.Paralyzed;
        }

        if (paralyzed.Disarmed || !canDisarm)
            return KsShoveOutcome.Pushed;

        paralyzed.Disarmed = true;
        Dirty(target, paralyzed);

        foreach (var hand in _handsSystem.EnumerateHands(target).ToList())
        {
            // Grabs and carries live in hands as virtual items: dropping those isn't a disarm.
            if (_handsSystem.TryGetHeldItem(target, hand, out var held) && !HasComp<VirtualItemComponent>(held))
                _handsSystem.TryDrop(target, hand, checkActionBlocker: false);
        }

        PopupShove(shover, target, "ks-shove-disarm-");
        _adminLogger.Add(LogType.DisarmedAction, LogImpact.Medium,
            $"{ToPrettyString(shover):user} kicked everything out of {ToPrettyString(target):target}'s hands");
        return KsShoveOutcome.Disarmed;
    }

    private void PopupShove(EntityUid shover, EntityUid target, string prefix)
    {
        var shoverName = Identity.Entity(shover, EntityManager);
        var targetName = Identity.Entity(target, EntityManager);
        _ksPopupSystem.PopupUserTargetOthers(target, shover, target,
            KsPopupMessage.Create(prefix + "user", ("target", targetName)),
            KsPopupMessage.Create(prefix + "target", ("shover", shoverName)),
            KsPopupMessage.Create(prefix + "others", ("shover", shoverName), ("target", targetName)),
            PopupType.MediumCaution);
    }

    private bool TryGetObstacle(EntityUid shover, EntityUid target, MapCoordinates probe,
        [NotNullWhen(true)] out Entity<KsShoveObstacleComponent>? obstacle)
    {
        obstacle = null;
        _obstacles.Clear();
        _entityLookupSystem.GetEntitiesInRange(probe, Comp<ComplexShoveComponent>(shover).ProbeRadius, _obstacles);

        foreach (var candidate in _obstacles)
        {
            if (candidate.Owner == shover || candidate.Owner == target)
                continue;

            if (obstacle == null || candidate.Comp.Priority > obstacle.Value.Comp.Priority)
                obstacle = candidate;
        }

        return obstacle != null;
    }

    /// <summary>
    ///     Knocks <paramref name="target"/> over against <paramref name="obstacle"/>, as a shove into it does, and
    ///         whoever the obstacle is with it. Also used for a grabbed person thrown into one.
    /// </summary>
    /// <param name="source">Who sent them flying, for the popups, sounds and logs.</param>
    /// <param name="popupPrefix">Replaces the obstacle's popups with <c>prefix + user/target/others</c>.</param>
    public void KnockIntoObstacle(EntityUid source, EntityUid target, Entity<KsShoveObstacleComponent> obstacle,
        string? popupPrefix = null)
    {
        var collide = new KsShoveCollideEvent(source, target, obstacle);
        RaiseLocalEvent(obstacle, ref collide);
        if (collide.Handled)
            return;

        if (obstacle.Comp.PlaceOnto && HasComp<ClimbableComponent>(obstacle))
            _climbSystem.Climb(target, source, obstacle, silent: true);

        _stunSystem.TryKnockdown(target, obstacle.Comp.KnockdownTime, drop: false);
        if (obstacle.Comp.CollateralKnockdownTime is { } collateralTime)
            _stunSystem.TryKnockdown(obstacle.Owner, collateralTime, drop: false);

        _audioSystem.PlayPredicted(obstacle.Comp.Sound, obstacle, source, AudioParams.Default.WithVariation(0.05f));

        var shoverName = Identity.Entity(source, EntityManager);
        var targetName = Identity.Entity(target, EntityManager);
        var obstacleName = Identity.Entity(obstacle, EntityManager);
        _ksPopupSystem.PopupUserTargetOthers(target, source, target,
            KsPopupMessage.Create(popupPrefix != null ? popupPrefix + "user" : obstacle.Comp.UserPopup,
                ("shover", shoverName), ("target", targetName), ("obstacle", obstacleName)),
            KsPopupMessage.Create(popupPrefix != null ? popupPrefix + "target" : obstacle.Comp.TargetPopup,
                ("shover", shoverName), ("target", targetName), ("obstacle", obstacleName)),
            KsPopupMessage.Create(popupPrefix != null ? popupPrefix + "others" : obstacle.Comp.OthersPopup,
                ("shover", shoverName), ("target", targetName), ("obstacle", obstacleName)),
            PopupType.MediumCaution);

        _adminLogger.Add(LogType.DisarmedKnockdown, LogImpact.Medium,
            $"{ToPrettyString(source):user} sent {ToPrettyString(target):target} into {ToPrettyString(obstacle):obstacle}");
    }

    /// <summary>
    ///     Knocks <paramref name="target"/> down against a wall, or anything solid with no obstacle component.
    /// </summary>
    /// <param name="popupPrefix">Popups are <c>prefix + user/target/others</c>, or <c>prefix + resisted-...</c> when
    ///     the knockdown didn't take, such as while weightless.</param>
    public void KnockIntoWall(EntityUid source, EntityUid target, TimeSpan knockdownTime, SoundSpecifier? sound,
        string popupPrefix)
    {
        var knockedDown = _stunSystem.TryKnockdown(target, knockdownTime, drop: false);
        _audioSystem.PlayPredicted(sound, target, source, AudioParams.Default.WithVariation(0.05f));
        PopupShove(source, target, knockedDown ? popupPrefix : popupPrefix + "resisted-");

        if (knockedDown)
        {
            _adminLogger.Add(LogType.DisarmedKnockdown, LogImpact.Medium,
                $"{ToPrettyString(source):user} sent {ToPrettyString(target):target} into a wall");
        }
    }

    private bool IsBlocked(Entity<ComplexShoveComponent> shover, MapCoordinates targetPosition, Vector2 direction)
    {
        var result = _rayCastSystem.CastRayClosest(
            targetPosition.MapId,
            targetPosition.Position,
            direction * shover.Comp.ProbeDistance,
            new QueryFilter { LayerBits = 0L, Flags = QueryFlags.Static, MaskBits = shover.Comp.CollisionMask });

        return result.Hit;
    }

    private void Push(Entity<ComplexShoveComponent> shover, EntityUid target, Vector2 direction, bool targetDown)
    {
        var pushPower = targetDown ? shover.Comp.DownedPushPower : shover.Comp.StandingPushPower;
        _physicsSystem.ApplyLinearImpulse(target, direction * pushPower);
    }

    private bool TryGetShoveDirection(EntityUid shover, EntityUid target, [NotNullWhen(true)] out Vector2? direction,
        [NotNullWhen(true)] out MapCoordinates? targetPosition)
    {
        direction = null;
        targetPosition = _transformSystem.GetMapCoordinates(target);
        var shoverPosition = _transformSystem.GetMapCoordinates(shover);
        if (shoverPosition.MapId != targetPosition.Value.MapId)
            return false;

        // trolling is not permitted
        var delta = targetPosition.Value.Position - shoverPosition.Position;
        if (delta.LengthSquared() <= float.Epsilon)
            return false;

        direction = delta.Normalized();
        return true;
    }
}
