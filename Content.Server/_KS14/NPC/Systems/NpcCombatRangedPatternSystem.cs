using System.Numerics;
using Content.Server._KS14.NPC.Components;
using Content.Shared._KS14.GenericSpriteFlick;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._KS14.NPC.Systems;

/// <summary>
///     Ranged attacks fired in patterns - spirals, fans, bursts in every direction - for bosses and the like, on top of
///         ordinary NPC ranged combat. HTN starts one by id (<see cref="TryStartAttack"/>); it then fires burst by
///         burst, <see cref="NpcRangedAttackPatternComponent.ShotDelay"/> apart, from
///         <see cref="NpcActiveRangedAttackComponent"/>, which the NPC carries only while it is firing. Once the last
///         burst is out, the attack goes on cooldown.
/// </summary>
public sealed partial class NpcCombatRangedPatternSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IRobustRandom _robustRandom = default!;
    [Dependency] private KsGenericSpriteFlickSystem _genericSpriteFlickSystem = default!;
    [Dependency] private SharedAudioSystem _audioSystem = default!;
    [Dependency] private SharedGunSystem _gunSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    [Dependency] private EntityQuery<NpcActiveRangedAttackComponent> _activeAttackQuery = default!;
    [Dependency] private EntityQuery<NpcRangedAttackPatternHolderComponent> _holderQuery = default!;

    private static readonly Vector2[] CardinalDirections =
    [
        new(0, 1),
        new(1, 0),
        new(0, -1),
        new(-1, 0),
    ];

    private static readonly Vector2[] DiagonalDirections =
    [
        new Vector2(1, 1).Normalized(),
        new Vector2(1, -1).Normalized(),
        new Vector2(-1, -1).Normalized(),
        new Vector2(-1, 1).Normalized(),
    ];

    /// <summary>
    ///     Whether <paramref name="uid"/> is part way through an attack, with bursts still to fire.
    /// </summary>
    public bool IsAttackActive(EntityUid uid)
    {
        return _activeAttackQuery.HasComp(uid);
    }

    /// <summary>
    ///     Starts <paramref name="entity"/>'s attack <paramref name="attackId"/>, at <paramref name="targetUid"/> if
    ///         given: telegraphed and sounded now, its first burst on the next update. False if it has no such attack,
    ///         the attack is on cooldown, or it is part way through one already.
    /// </summary>
    public bool TryStartAttack(Entity<NpcRangedAttackPatternHolderComponent?> entity, string attackId, EntityUid? targetUid)
    {
        if (!_holderQuery.Resolve(entity.Owner, ref entity.Comp, logMissing: false) ||
            _activeAttackQuery.HasComp(entity) ||
            !entity.Comp.Attacks.TryGetValue(attackId, out var attackProtoId) ||
            entity.Comp.CooldownEnds.TryGetValue(attackId, out var cooldownEnd) && _gameTiming.CurTime < cooldownEnd ||
            !ProtoMan.TryIndex(attackProtoId, out var attackPrototype) ||
            !attackPrototype.TryComp<NpcRangedAttackPatternComponent>(out var pattern, Factory))
            return false;

        if (pattern.TelegraphSpriteFlickData is { } spriteFlickData)
            _genericSpriteFlickSystem.Flick(entity, spriteFlickData);

        if (pattern.Sound != null)
            _audioSystem.PlayPvs(pattern.Sound, entity);

        var activeAttackComponent = AddComp<NpcActiveRangedAttackComponent>(entity);
        activeAttackComponent.State = new NpcRangedAttackState(attackId, pattern, targetUid);
        activeAttackComponent.NextBurstAt = _gameTiming.CurTime;
        return true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _gameTiming.CurTime;
        var activeAttackEnumerator = EntityQueryEnumerator<NpcActiveRangedAttackComponent, NpcRangedAttackPatternHolderComponent>();
        while (activeAttackEnumerator.MoveNext(out var uid, out var activeAttackComponent, out var holderComponent))
        {
            // Finished, and on its way out at the end of the tick.
            if (activeAttackComponent.LifeStage > ComponentLifeStage.Running || now < activeAttackComponent.NextBurstAt)
                continue;

            ref var state = ref activeAttackComponent.State;
            FireBurst(uid, ref state);
            state.BurstsFired++;

            if (state.BurstsFired < state.Pattern.BurstCount)
            {
                activeAttackComponent.NextBurstAt = now + state.Pattern.ShotDelay;
                continue;
            }

            holderComponent.CooldownEnds[state.AttackId] = now + state.Pattern.Cooldown;
            RemCompDeferred(uid, activeAttackComponent);
        }
    }

    private void FireBurst(EntityUid uid, ref NpcRangedAttackState state)
    {
        var pattern = state.Pattern;

        switch (pattern.AttackType)
        {
            case NpcRangedType.Single:
                if (GetDirectionToTarget(uid, state.TargetUid) is { } towardsTarget)
                    FireAll(uid, pattern, towardsTarget);
                break;
            case NpcRangedType.Spiral:
            case NpcRangedType.Wave:
                state.Angle += MathHelper.DegreesToRadians(pattern.DegreesPerShot);
                FireAll(uid, pattern, new Angle(state.Angle).ToVec());
                break;
            case NpcRangedType.DoubleSpiral:
                state.Angle += MathHelper.DegreesToRadians(pattern.DegreesPerShot);
                Fire(uid, pattern, new Angle(state.Angle).ToVec());
                Fire(uid, pattern, new Angle(state.Angle + MathHelper.DegreesToRadians(pattern.RotationOffset)).ToVec());
                break;
            case NpcRangedType.Shotgun:
                FireSpread(uid, pattern, GetDirectionToTarget(uid, state.TargetUid) ?? _transformSystem.GetWorldRotation(uid).ToWorldVec());
                break;
            case NpcRangedType.Cone:
            case NpcRangedType.TargetedBurst:
                if (GetDirectionToTarget(uid, state.TargetUid) is { } coneDirection)
                    FireSpread(uid, pattern, coneDirection);
                break;
            case NpcRangedType.CardinalDirections:
                // Alternating: the cardinals, then the diagonals.
                FireEach(uid, pattern, state.BurstsFired % 2 == 0 ? CardinalDirections : DiagonalDirections);
                break;
            case NpcRangedType.DiagonalDirections:
                FireEach(uid, pattern, DiagonalDirections);
                break;
            case NpcRangedType.AllDirections:
                FireEach(uid, pattern, CardinalDirections, pattern.Shots * CardinalDirections.Length);
                FireEach(uid, pattern, DiagonalDirections, pattern.Shots * DiagonalDirections.Length);
                break;
            case NpcRangedType.RandomAoe:
                for (var i = 0; i < pattern.Shots; i++)
                {
                    Fire(uid, pattern, _robustRandom.NextAngle().ToVec());
                }

                break;
            case NpcRangedType.RapidFire:
                if (GetDirectionToTarget(uid, state.TargetUid) is not { } aim)
                    break;

                for (var i = 0; i < pattern.Shots; i++)
                {
                    var offset = Angle.FromDegrees(_robustRandom.NextFloat(-pattern.Spread, pattern.Spread));
                    Fire(uid, pattern, offset.RotateVec(aim));
                }

                break;
        }
    }

    /// <summary>
    ///     <see cref="NpcRangedAttackPatternComponent.Shots"/> projectiles, all along <paramref name="direction"/>.
    /// </summary>
    private void FireAll(EntityUid uid, NpcRangedAttackPatternComponent pattern, Vector2 direction)
    {
        for (var i = 0; i < pattern.Shots; i++)
        {
            Fire(uid, pattern, direction);
        }
    }

    /// <summary>
    ///     <paramref name="count"/> projectiles - <see cref="NpcRangedAttackPatternComponent.Shots"/> if not given -
    ///         going round <paramref name="directions"/>.
    /// </summary>
    private void FireEach(EntityUid uid, NpcRangedAttackPatternComponent pattern, Vector2[] directions, int? count = null)
    {
        var total = count ?? pattern.Shots;
        for (var i = 0; i < total; i++)
        {
            Fire(uid, pattern, directions[i % directions.Length]);
        }
    }

    /// <summary>
    ///     <see cref="NpcRangedAttackPatternComponent.Shots"/> projectiles fanned across
    ///         <see cref="NpcRangedAttackPatternComponent.Spread"/>, centred on <paramref name="direction"/>.
    /// </summary>
    private void FireSpread(EntityUid uid, NpcRangedAttackPatternComponent pattern, Vector2 direction)
    {
        var step = MathHelper.DegreesToRadians(pattern.Spread) / pattern.Shots;
        for (var i = 0; i < pattern.Shots; i++)
        {
            var offset = new Angle((i - (pattern.Shots - 1) / 2f) * step);
            Fire(uid, pattern, offset.RotateVec(direction));
        }
    }

    private void Fire(EntityUid uid, NpcRangedAttackPatternComponent pattern, Vector2 direction)
    {
        var projectileUid = Spawn(pattern.Projectile, Transform(uid).Coordinates);
        _gunSystem.ShootProjectile(projectileUid, direction, Vector2.Zero, uid, uid, pattern.Speed);
    }

    /// <summary>
    ///     Which way <paramref name="targetUid"/> is from <paramref name="uid"/>, if there is a target on the same map
    ///         and not right on top of it.
    /// </summary>
    private Vector2? GetDirectionToTarget(EntityUid uid, EntityUid? targetUid)
    {
        if (targetUid is not { } target || TerminatingOrDeleted(target))
            return null;

        var ownerMapCoordinates = _transformSystem.GetMapCoordinates(uid);
        var targetMapCoordinates = _transformSystem.GetMapCoordinates(target);
        if (ownerMapCoordinates.MapId != targetMapCoordinates.MapId)
            return null;

        var direction = targetMapCoordinates.Position - ownerMapCoordinates.Position;
        return direction.LengthSquared() < 0.0001f ? null : direction.Normalized();
    }
}
