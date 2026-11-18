using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using Content.Shared._KS14.KsPopup;
using Content.Shared.Administration.Logs;
using Content.Shared.Buckle;
using Content.Shared.Climbing.Components;
using Content.Shared.Climbing.Systems;
using Content.Shared.CombatMode;
using Content.Shared.Damage.Components;
using Content.Shared.Database;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.IdentityManagement;
using Content.Shared.Inventory.VirtualItem;
using Content.Shared.Jittering;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Standing;
using Content.Shared.StatusEffectNew;
using Content.Shared.Stunnable;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Collections;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Physics.Systems;
using DependencyAttribute = Robust.Shared.IoC.DependencyAttribute;

namespace Content.Shared._KS14.ComplexShove;

/// <summary>
///     tgstation's shove. Resolved in this order:
///     <list type="number">
///         <item>Someone buckled to something is unaffected.</item>
///         <item>Someone standing in something that takes them, an open closet, goes into it.</item>
///         <item>Someone in crit or stamina crit is only pushed.</item>
///         <item><see cref="KsShoveAttemptEvent"/> lets something else take the shove, as a grab does to pin its victim.</item>
///         <item>Someone already down is kicked onto their side and paralyzed, once until they stand up.</item>
///         <item>Someone shoved into a <see cref="KsShoveObstacleComponent"/> (a table, another person) is knocked over by it.</item>
///         <item>Someone shoved against a wall or anything else solid is knocked down, if an earlier shove staggered
///             them; if not, they are pushed and staggered.</item>
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
    [Dependency] private INetManager _netManager = default!;
    [Dependency] private SharedBuckleSystem _buckleSystem = default!;
    [Dependency] private SharedHandsSystem _handsSystem = default!;
    [Dependency] private SharedJitteringSystem _jitteringSystem = default!;
    [Dependency] private SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private SharedStunSystem _stunSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private StandingStateSystem _standingStateSystem = default!;
    [Dependency] private StatusEffectsSystem _statusEffectsSystem = default!;

    /// <summary>
    ///     The stock disarm's popups (<c>prefix + popup-message-...</c>), kept for a shove that only pushes.
    /// </summary>
    private const string PushPopupPrefix = "disarm-action-shove-";

    /// <summary>
    ///     Scratch set for <see cref="TryGetObstacle"/>.
    /// </summary>
    private readonly HashSet<Entity<KsShoveObstacleComponent>> _obstacles = new();

    // StoodEvent is a by-value class event, which has no Entity<T> handler form.
    [SubscribeLocalEvent]
    private void OnStood(EntityUid uid, KsShoveParalyzedComponent component, StoodEvent args)
    {
        RemCompDeferred<KsShoveParalyzedComponent>(uid);
    }

    [SubscribeLocalEvent]
    private void OnDisarmed(Entity<StandingStateComponent> target, ref DisarmedEvent args)
    {
        if (args.Handled || !TryComp<ComplexShoveComponent>(args.Source, out var shoveComponent) ||
            !TryGetShoveDirection(args.Source, target, out var direction, out var targetPosition))
        {
            return;
        }

        args.Handled = true;
        Entity<ComplexShoveComponent> shover = (args.Source, shoveComponent);
        var outcome = ResolveShove(shover, target, direction.Value, targetPosition.Value, out var obstacle);

        // A plain push, or a shove that came to nothing, keeps the stock popups. Every other outcome has already told
        // everyone what happened.
        if (outcome is KsShoveOutcome.Pushed or KsShoveOutcome.Unmoved)
            args.PopupPrefix = PushPopupPrefix;
        else
            args.KsPopupHandled = true;

        var shovedEvent = new KsShovedEvent(args.Source, target, outcome, obstacle);
        RaiseLocalEvent(target, ref shovedEvent);
    }

    private KsShoveOutcome ResolveShove(Entity<ComplexShoveComponent> shover, Entity<StandingStateComponent> target,
        Vector2 direction, MapCoordinates targetPosition, out EntityUid? obstacle)
    {
        obstacle = null;

        // Someone strapped in stays put: a seat, a bed or a rollerbed holds them against any shove.
        if (_buckleSystem.IsBuckled(target))
            return KsShoveOutcome.Unmoved;

        // Someone standing in something that takes them, an open closet, goes into it whatever else is true of them.
        if (TryShoveIntoStoodIn(shover, target, targetPosition, out obstacle))
            return KsShoveOutcome.Obstacle;

        // Someone already out of the fight, in crit or stamina crit, is only pushed: nothing to knock down, kick or pin.
        if (_mobStateSystem.IsIncapacitated(target) || CompOrNull<StaminaComponent>(target)?.Critical == true)
        {
            Push(shover, target, direction, targetDown: true);
            return KsShoveOutcome.Pushed;
        }

        var attempt = new KsShoveAttemptEvent(shover, target);
        RaiseLocalEvent(target, ref attempt);
        if (attempt.Handled)
            return KsShoveOutcome.Handled;

        // Someone already down can't be knocked down again: they're kicked over, then disarmed against a wall, then only
        // pushed, with no stagger either: that waits until they've stood up.
        if (_standingStateSystem.IsDown(target.AsNullable()))
        {
            // Knocked down again they can't be, but they can still be sent onto a table, or into a closet, behind them.
            if (TryGetObstacle(shover, target, targetPosition, direction, out var behind) &&
                TrySendDownedInto(shover, target, targetPosition, behind.Value))
            {
                obstacle = behind.Value.Owner;
                return KsShoveOutcome.Obstacle;
            }

            var downOutcome = KickWhileDown(shover, target, targetPosition, direction);
            if (downOutcome == KsShoveOutcome.Pushed)
                Push(shover, target, direction, targetDown: true);

            return downOutcome;
        }

        if (TryGetObstacle(shover, target, targetPosition, direction, out var found))
        {
            obstacle = found.Value.Owner;
            KnockIntoObstacle(shover, target, found.Value);
            return KsShoveOutcome.Obstacle;
        }

        // Every shove pushes, wall or no wall.
        Push(shover, target, direction, targetDown: false);

        // A wall puts down someone already staggered by a shove. The first shove against one only staggers them.
        if (IsBlocked(shover, targetPosition, direction) &&
            (!shover.Comp.BlockedKnockdownNeedsStagger ||
             _statusEffectsSystem.HasStatusEffect(target, shover.Comp.StaggerEffect)))
        {
            KnockIntoWall(shover, target, shover.Comp.BlockedKnockdownTime, shover.Comp.BlockedSound,
                shover.Comp.BlockedPopups, shover.Comp.BlockedResistedPopups);
            return KsShoveOutcome.Blocked;
        }

        _movementModStatusSystem.TryUpdateMovementSpeedModDuration(target,
            shover.Comp.StaggerEffect,
            shover.Comp.StaggerTime,
            shover.Comp.StaggerSpeedModifier);

        if (shover.Comp.StaggerJitterAmplitude > 0f)
        {
            _jitteringSystem.DoJitter(target, shover.Comp.StaggerTime, refresh: true,
                amplitude: shover.Comp.StaggerJitterAmplitude, frequency: shover.Comp.StaggerJitterFrequency);
        }

        return KsShoveOutcome.Pushed;
    }

    /// <summary>
    ///     Whether what is behind <paramref name="target"/> lets a shove knock things out of their hands: a wall, by
    ///         <see cref="ComplexShoveComponent.DisarmAgainstWalls"/>, or an obstacle with
    ///         <see cref="KsShoveObstacleComponent.DisarmsAgainst"/>. <see cref="KsCanShoveDisarmEvent"/> has the last word.
    /// </summary>
    public bool CanDisarmAgainst(Entity<ComplexShoveComponent> shover, EntityUid target, MapCoordinates targetPosition,
        Vector2 direction)
    {
        EntityUid? surface = null;
        var allowed = false;

        if (TryGetObstacle(shover, target, targetPosition, direction, out var obstacle))
        {
            surface = obstacle.Value.Owner;
            allowed = obstacle.Value.Comp.DisarmsAgainst;
        }
        else if (IsBlocked(shover, targetPosition, direction))
        {
            allowed = shover.Comp.DisarmAgainstWalls;
        }

        var check = new KsCanShoveDisarmEvent(shover, target, surface, allowed);
        RaiseLocalEvent(target, ref check);
        return check.Allowed;
    }

    /// <summary>
    ///     A shove on someone lying down: the first kicks them onto their side and paralyzes them, the next, against a
    ///         wall, makes them drop everything they hold. Neither repeats until they have stood up again.
    /// </summary>
    /// <returns><see cref="KsShoveOutcome.Pushed"/> when there is nothing left to do but push them.</returns>
    private KsShoveOutcome KickWhileDown(Entity<ComplexShoveComponent> shover, EntityUid target,
        MapCoordinates targetPosition, Vector2 direction)
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
            PopupShove(shover, target, shover.Comp.ParalyzePopups);
            _adminLogger.Add(LogType.DisarmedKnockdown, LogImpact.Medium,
                $"{ToPrettyString(shover):user} kicked {ToPrettyString(target):target} onto their side");
            return KsShoveOutcome.Paralyzed;
        }

        if (paralyzed.Disarmed || !CanDisarmAgainst(shover, target, targetPosition, direction))
            return KsShoveOutcome.Pushed;

        paralyzed.Disarmed = true;
        Dirty(target, paralyzed);

        foreach (var hand in _handsSystem.EnumerateHands(target).ToList())
        {
            // Grabs and carries live in hands as virtual items: dropping those isn't a disarm.
            if (_handsSystem.TryGetHeldItem(target, hand, out var held) && !HasComp<VirtualItemComponent>(held))
                _handsSystem.TryDrop(target, hand, checkActionBlocker: false);
        }

        PopupShove(shover, target, shover.Comp.DisarmPopups);
        _adminLogger.Add(LogType.DisarmedAction, LogImpact.Medium,
            $"{ToPrettyString(shover):user} kicked everything out of {ToPrettyString(target):target}'s hands");
        return KsShoveOutcome.Disarmed;
    }

    /// <param name="predicted">False where the source's client can't tell on which tick this happens, as in a
    ///     collision in flight: its own popup is then never matched with the server's, and shows twice. The server
    ///     then tells everyone and no client says anything itself.</param>
    private void PopupShove(EntityUid shover, EntityUid target, KsAudiencePopups popups, bool predicted = true)
    {
        if (!predicted && _netManager.IsClient)
            return;

        _ksPopupSystem.PopupUserTargetOthers(target, shover, target, popups, PopupType.MediumCaution,
            ("shover", Identity.Entity(shover, EntityManager)),
            ("target", Identity.Entity(target, EntityManager)));
    }

    /// <summary>
    ///     Offers the shove to each <see cref="KsShoveObstacleComponent.TakesWhoStandsInIt"/> obstacle the target stands
    ///         in, highest priority first, until one handles its <see cref="KsShoveCollideEvent"/>.
    /// </summary>
    private bool TryShoveIntoStoodIn(Entity<ComplexShoveComponent> shover, EntityUid target, MapCoordinates targetPosition,
        out EntityUid? obstacle)
    {
        obstacle = null;
        _obstacles.Clear();
        _entityLookupSystem.GetEntitiesInRange(targetPosition, shover.Comp.ProbeRadius, _obstacles);

        // Copied out: a handler may shove something on, which looks obstacles up again.
        var stoodIn = new ValueList<Entity<KsShoveObstacleComponent>>();
        foreach (var candidate in _obstacles)
        {
            if (candidate.Comp.TakesWhoStandsInIt && candidate.Owner != shover.Owner && candidate.Owner != target)
                stoodIn.Add(candidate);
        }

        stoodIn.Sort(static (a, b) => b.Comp.Priority.CompareTo(a.Comp.Priority));
        foreach (var candidate in stoodIn)
        {
            if (!RaiseCollide(shover, target, candidate))
                continue;

            obstacle = candidate.Owner;
            return true;
        }

        return false;
    }

    /// <summary>
    ///     Lets the obstacle replace what being sent into it does.
    /// </summary>
    /// <returns>Whether it did.</returns>
    private bool RaiseCollide(EntityUid source, EntityUid target, EntityUid obstacle)
    {
        var collide = new KsShoveCollideEvent(source, target, obstacle);
        RaiseLocalEvent(obstacle, ref collide);
        return collide.Handled;
    }

    /// <summary>
    ///     The <see cref="KsShoveObstacleComponent"/> behind <paramref name="target"/>, at the end of the shover's probe.
    /// </summary>
    private bool TryGetObstacle(Entity<ComplexShoveComponent> shover, EntityUid target, MapCoordinates targetPosition,
        Vector2 direction, [NotNullWhen(true)] out Entity<KsShoveObstacleComponent>? obstacle)
    {
        obstacle = null;
        _obstacles.Clear();
        var probe = targetPosition.Offset(direction * shover.Comp.ObstacleProbeDistance);
        _entityLookupSystem.GetEntitiesInRange(probe, shover.Comp.ProbeRadius, _obstacles);

        foreach (var candidate in _obstacles)
        {
            if (candidate.Owner == shover.Owner || candidate.Owner == target)
                continue;

            if (candidate.Comp.IgnoredWhileDown && _standingStateSystem.IsDown(candidate.Owner))
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
    /// <param name="popups">Shown instead of the obstacle's own <see cref="KsShoveObstacleComponent.Popups"/>.</param>
    /// <param name="predictedAnnouncement">Whether the source's client shows the popups and plays the sound itself,
    ///     ahead of the server. False for a collision in flight, which client and server rarely have on the same tick:
    ///     the server then tells everyone, the source included.</param>
    public void KnockIntoObstacle(EntityUid source, EntityUid target, Entity<KsShoveObstacleComponent> obstacle,
        KsAudiencePopups? popups = null, bool predictedAnnouncement = true)
    {
        if (RaiseCollide(source, target, obstacle))
            return;

        if (CanPlaceOnto(obstacle))
            _climbSystem.Climb(target, source, obstacle, silent: true);

        _stunSystem.TryKnockdown(target, obstacle.Comp.KnockdownTime, drop: false);
        if (obstacle.Comp.CollateralKnockdownTime is { } collateralTime)
            _stunSystem.TryKnockdown(obstacle.Owner, collateralTime, drop: false);

        AnnounceObstacle(source, target, obstacle, popups ?? obstacle.Comp.Popups, predictedAnnouncement);
    }

    /// <summary>
    ///     A shove on someone lying down, with an obstacle behind them. They aren't knocked down again, so only an
    ///         obstacle that takes them does anything: one they are put onto, as a table, or one whose
    ///         <see cref="KsShoveCollideEvent"/> handler takes the shove, as an open closet.
    /// </summary>
    private bool TrySendDownedInto(Entity<ComplexShoveComponent> shover, EntityUid target, MapCoordinates targetPosition,
        Entity<KsShoveObstacleComponent> obstacle)
    {
        // Lying on it already: this is a shove along it, not onto it.
        var obstaclePosition = _transformSystem.GetMapCoordinates(obstacle);
        if ((obstaclePosition.Position - targetPosition.Position).Length() < shover.Comp.OnObstacleDistance)
            return false;

        if (RaiseCollide(shover, target, obstacle))
            return true;

        if (!CanPlaceOnto(obstacle))
            return false;

        _climbSystem.Climb(target, shover, obstacle, silent: true);
        AnnounceObstacle(shover, target, obstacle, obstacle.Comp.Popups);
        return true;
    }

    private bool CanPlaceOnto(Entity<KsShoveObstacleComponent> obstacle)
    {
        return obstacle.Comp.PlaceOnto && HasComp<ClimbableComponent>(obstacle);
    }

    /// <summary>
    ///     The sound, popups and log of someone being sent into an obstacle.
    /// </summary>
    private void AnnounceObstacle(EntityUid source, EntityUid target, Entity<KsShoveObstacleComponent> obstacle,
        KsAudiencePopups popups, bool predicted = true)
    {
        PlayShoveSound(obstacle.Comp.Sound, obstacle, source, predicted);

        if (predicted || _netManager.IsServer)
        {
            _ksPopupSystem.PopupUserTargetOthers(target, source, target, popups, PopupType.MediumCaution,
                ("shover", Identity.Entity(source, EntityManager)),
                ("target", Identity.Entity(target, EntityManager)),
                ("obstacle", Identity.Entity(obstacle, EntityManager)));
        }

        _adminLogger.Add(LogType.DisarmedKnockdown, LogImpact.Medium,
            $"{ToPrettyString(source):user} sent {ToPrettyString(target):target} into {ToPrettyString(obstacle):obstacle}");
    }

    /// <summary>
    ///     Plays a shove's sound. Predicted, the source's client plays it at once and the server leaves the source
    ///         out; unpredicted, only the server plays it, to everyone.
    /// </summary>
    private void PlayShoveSound(SoundSpecifier? sound, EntityUid at, EntityUid source, bool predicted)
    {
        var audioParams = AudioParams.Default.WithVariation(0.05f);
        if (predicted)
            _audioSystem.PlayPredicted(sound, at, source, audioParams);
        else if (_netManager.IsServer)
            _audioSystem.PlayPvs(sound, at, audioParams);
    }

    /// <summary>
    ///     Knocks <paramref name="target"/> down against a wall, or anything solid with no obstacle component.
    /// </summary>
    /// <param name="resistedPopups">Shown instead of <paramref name="popups"/> when the knockdown didn't take, such as
    ///     while weightless.</param>
    /// <param name="downedPopups">Shown instead of <paramref name="popups"/> when they were lying down already, as
    ///     someone thrown may be: they can't be said to go down. Without it, <paramref name="popups"/> is shown.</param>
    /// <param name="predictedAnnouncement">As for <see cref="KnockIntoObstacle"/>.</param>
    public void KnockIntoWall(EntityUid source, EntityUid target, TimeSpan knockdownTime, SoundSpecifier? sound,
        KsAudiencePopups popups, KsAudiencePopups resistedPopups, KsAudiencePopups? downedPopups = null,
        bool predictedAnnouncement = true)
    {
        var wasDown = _standingStateSystem.IsDown(target);
        var knockedDown = _stunSystem.TryKnockdown(target, knockdownTime, drop: false);
        PlayShoveSound(sound, target, source, predictedAnnouncement);

        if (!knockedDown)
            PopupShove(source, target, resistedPopups, predictedAnnouncement);
        else if (wasDown && downedPopups != null)
            PopupShove(source, target, downedPopups.Value, predictedAnnouncement);
        else
            PopupShove(source, target, popups, predictedAnnouncement);

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
