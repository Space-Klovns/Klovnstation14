using Content.Server._KS14.NPC.Components;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;
using System.Numerics;
using Robust.Shared.Player;
using Content.Shared._KS14.GenericSpriteFlick;

namespace Content.Server._KS14.NPC.Systems;

/// <summary>
/// Extension system for NPC ranged combat that adds pattern-based attacks on top of the existing NPCCombatSystem
/// </summary>
public sealed partial class NPCCombatRangedPatternSystem : EntitySystem
{
    [Dependency] private EntityQuery<TransformComponent> _xformQuery = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedGunSystem _gunSystem = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private KsGenericSpriteFlickSystem _genericSpriteFlickSystem = default!;

    private readonly Dictionary<EntityUid, NPCRangedState> _activeAttacks = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NpcRangedAttackPatternHolderComponent, ComponentShutdown>(OnHolderShutdown);
    }

    private void OnHolderShutdown(EntityUid uid, NpcRangedAttackPatternHolderComponent component, ComponentShutdown args)
    {
        // Clean up any active attacks when the holder is removed
        if (_activeAttacks.ContainsKey(uid))
        {
            _activeAttacks.Remove(uid);
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        UpdateActiveAttacks();
    }

    private void UpdateActiveAttacks()
    {
        if (_activeAttacks.Count == 0)
            return;

        var currentTime = (float)_timing.CurTime.TotalSeconds;
        var completedAttacks = new List<EntityUid>();

        foreach (var (uid, state) in _activeAttacks)
        {
            // Owner may have died/been deleted mid-attack
            if (!Exists(uid) || Deleted(uid))
            {
                completedAttacks.Add(uid);
                continue;
            }

            // Check if attack is complete
            if (state.CurrentShot >= state.Attack.BurstCount)
            {
                completedAttacks.Add(uid);
                continue;
            }

            // Check if it's time for the next shot
            if (currentTime >= state.NextShotTime)
            {
                ExecuteAttackPattern(state);
                state.CurrentShot++;
                state.NextShotTime = currentTime + state.Attack.ShotDelay;
            }
        }

        foreach (var uid in completedAttacks)
        {
            CompleteAttack(uid);
        }
    }

    private void CompleteAttack(EntityUid owner)
    {
        if (!_activeAttacks.TryGetValue(owner, out var state))
            return;

        _activeAttacks.Remove(owner);

        // Owner might be deleted
        if (!Exists(owner) || Deleted(owner))
            return;

        // Clean up attack entity
        if (Exists(state.AttackEntity) && !Deleted(state.AttackEntity))
        {
            Del(state.AttackEntity);
        }

        // Set cooldown
        if (TryComp<NpcRangedAttackPatternHolderComponent>(owner, out var holder))
        {
            var currentTime = (float)_timing.CurTime.TotalSeconds;
            holder.Cooldowns[state.AttackId] = currentTime + state.Attack.Cooldown;
            Logger.Debug($"Attack {state.AttackId} completed. Cooldown set to {holder.Cooldowns[state.AttackId]}");
        }
    }

    public float GetCooldownEndTime(EntityUid owner, string attackId)
    {
        if (!TryComp<NpcRangedAttackPatternHolderComponent>(owner, out var holder))
            return 0f;

        if (holder.Cooldowns.TryGetValue(attackId, out var cooldownEnd))
            return cooldownEnd;

        return 0f;
    }

    /// <summary>
    /// True while this owner still has an attack mid-sequence (bursts left to fire).
    /// </summary>
    public bool IsAttackActive(EntityUid owner)
    {
        return _activeAttacks.ContainsKey(owner);
    }

    /// <summary>
    /// Executes an NPC attack by ID from the NPC's attack holder
    /// </summary>
    public bool ExecuteAttack(EntityUid owner, string attackId, EntityUid? target = null)
    {
        if (!Exists(owner))
            return false;

        if (!TryComp<NpcRangedAttackPatternHolderComponent>(owner, out var holder))
            return false;

        if (!holder.Attacks.TryGetValue(attackId, out var attackProtoId))
            return false;

        var currentTime = (float)_timing.CurTime.TotalSeconds;
        if (holder.Cooldowns.TryGetValue(attackId, out var cooldownEnd) &&
            cooldownEnd > currentTime)
            return false;

        // Guard: never clobber an in-flight attack. This leaks the old
        // AttackEntity, sticks the telegraph sprite, and corrupts state.
        if (_activeAttacks.ContainsKey(owner))
            return false;

        var attackEnt = Spawn(attackProtoId, Transform(owner).Coordinates);
        if (!TryComp<NpcRangedAttackPatternComponent>(attackEnt, out var attack))
        {
            Del(attackEnt);
            return false;
        }

        // Show telegraph if enabled
        if (attack.TelegraphSpriteFlickData is { } spriteFlickData)
            _genericSpriteFlickSystem.Flick(owner, spriteFlickData);

        // Play sound
        if (attack.Sound != null)
        {
            _audio.PlayPvs(attack.Sound, owner);
        }

        // Create attack state - first shot happens immediately
        var state = new NPCRangedState
        {
            Owner = owner,
            AttackEntity = attackEnt,
            Attack = attack,
            Target = target,
            CurrentShot = 0,
            CurrentAngle = 0f,
            NextShotTime = currentTime,
            AttackId = attackId
        };

        _activeAttacks[owner] = state;
        Logger.Debug($"Attack {attackId} started for {owner}");

        return true;
    }

    private void ExecuteAttackPattern(NPCRangedState state)
    {
        switch (state.Attack.AttackType)
        {
            case NpcRangedType.Single:
                ExecuteSinglePattern(state);
                break;
            case NpcRangedType.Spiral:
                ExecuteSpiralPattern(state);
                break;
            case NpcRangedType.DoubleSpiral:
                ExecuteDoubleSpiralPattern(state);
                break;
            case NpcRangedType.Shotgun:
                ExecuteShotgunPattern(state);
                break;
            case NpcRangedType.CardinalDirections:
                ExecuteDirectionalPattern(state, false);
                break;
            case NpcRangedType.DiagonalDirections:
                ExecuteDirectionalPattern(state, true);
                break;
            case NpcRangedType.AllDirections:
                ExecuteAllDirectionsPattern(state);
                break;
            case NpcRangedType.RandomAoe:
                ExecuteRandomAoePattern(state);
                break;
            case NpcRangedType.Cone:
                ExecuteConePattern(state);
                break;
            case NpcRangedType.Wave:
                ExecuteWavePattern(state);
                break;
            case NpcRangedType.TargetedBurst:
                ExecuteTargetedBurstPattern(state);
                break;
            case NpcRangedType.RapidFire:
                ExecuteRapidFirePattern(state);
                break;
        }
    }

    private void ExecuteSinglePattern(NPCRangedState state)
    {
        var direction = GetDirectionToTarget(state) ?? Vector2.Zero;
        SpawnProjectile(state.Owner, state.Attack, direction);
    }

    private void ExecuteSpiralPattern(NPCRangedState state)
    {
        state.CurrentAngle += state.Attack.DegreesPerShot * (MathF.PI / 180f);
        var direction = new Vector2(MathF.Cos(state.CurrentAngle), MathF.Sin(state.CurrentAngle));
        SpawnProjectile(state.Owner, state.Attack, direction);
    }

    private void ExecuteDoubleSpiralPattern(NPCRangedState state)
    {
        state.CurrentAngle += state.Attack.DegreesPerShot * (MathF.PI / 180f);
        var rotationOffset = state.Attack.RotationOffset * (MathF.PI / 180f);

        var direction1 = new Vector2(MathF.Cos(state.CurrentAngle), MathF.Sin(state.CurrentAngle));
        SpawnProjectile(state.Owner, state.Attack, direction1);

        var direction2 = new Vector2(MathF.Cos(state.CurrentAngle + rotationOffset), MathF.Sin(state.CurrentAngle + rotationOffset));
        SpawnProjectile(state.Owner, state.Attack, direction2);
    }

    private void ExecuteShotgunPattern(NPCRangedState state)
    {
        var directionToTarget = GetDirectionToTarget(state) ?? new Vector2(1, 0);
        var baseAngle = MathF.Atan2(directionToTarget.Y, directionToTarget.X);
        var spreadRadians = state.Attack.Spread * (MathF.PI / 180f);

        for (int i = 0; i < state.Attack.Shots; i++)
        {
            var angleOffset = (i - (state.Attack.Shots - 1) / 2f) * (spreadRadians / state.Attack.Shots);
            var shotAngle = baseAngle + angleOffset;
            var direction = new Vector2(MathF.Cos(shotAngle), MathF.Sin(shotAngle));
            SpawnProjectile(state.Owner, state.Attack, direction);
        }
    }

    private Vector2? GetDirectionToTarget(NPCRangedState state)
    {
        if (state.Target == null || Deleted(state.Target) || !Exists(state.Target))
            return null;

        if (!_xformQuery.TryGetComponent(state.Owner, out var ownerXform) ||
            !_xformQuery.TryGetComponent(state.Target.Value, out var targetXform))
            return null;

        if (ownerXform.MapID != targetXform.MapID)
            return null;

        var worldPos = _transform.GetWorldPosition(ownerXform);
        var targetPos = _transform.GetWorldPosition(targetXform);

        var direction = targetPos - worldPos;
        if (direction.LengthSquared() < 0.0001f)
            return null;

        return direction.Normalized();
    }

    private void ExecuteDirectionalPattern(NPCRangedState state, bool diagonal)
    {
        // "Alternating" mode: diagonal == false means alternate
        // cardinal/diagonal per burst, like the original colossus.
        Vector2[] directions;

        if (diagonal)
        {
            directions = GetDiagonalDirections();
        }
        else
        {
            // Even burst -> cardinal, odd burst -> diagonal
            directions = state.CurrentShot % 2 == 0
                ? GetCardinalDirections()
                : GetDiagonalDirections();
        }

        for (int i = 0; i < state.Attack.Shots; i++)
        {
            var direction = directions[i % directions.Length];
            SpawnProjectile(state.Owner, state.Attack, direction);
        }

        state.CurrentShot++;
    }

    private void ExecuteAllDirectionsPattern(NPCRangedState state)
    {
        for (int i = 0; i < 8; i++)
        {
            var angle = i * 45f * (MathF.PI / 180f);
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            SpawnProjectile(state.Owner, state.Attack, direction);
        }
    }

    private void ExecuteRandomAoePattern(NPCRangedState state)
    {
        var random = new Random();
        var angle = random.Next(0, 360) * (MathF.PI / 180f);
        var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        SpawnProjectile(state.Owner, state.Attack, direction);
    }

    private void ExecuteConePattern(NPCRangedState state)
    {
        var directionToTarget = GetDirectionToTarget(state);
        if (directionToTarget == null) return;

        var baseAngle = MathF.Atan2(directionToTarget.Value.Y, directionToTarget.Value.X);
        var spreadRadians = state.Attack.Spread * (MathF.PI / 180f);

        for (int i = 0; i < state.Attack.Shots; i++)
        {
            var angleOffset = (i - (state.Attack.Shots - 1) / 2f) * (spreadRadians / state.Attack.Shots);
            var shotAngle = baseAngle + angleOffset;
            var direction = new Vector2(MathF.Cos(shotAngle), MathF.Sin(shotAngle));
            SpawnProjectile(state.Owner, state.Attack, direction);
        }
    }

    private void ExecuteWavePattern(NPCRangedState state)
    {
        state.CurrentAngle += state.Attack.DegreesPerShot * (MathF.PI / 180f);
        var direction = new Vector2(MathF.Cos(state.CurrentAngle), MathF.Sin(state.CurrentAngle));
        SpawnProjectile(state.Owner, state.Attack, direction);
    }

    private void ExecuteTargetedBurstPattern(NPCRangedState state)
    {
        var directionToTarget = GetDirectionToTarget(state);
        if (directionToTarget == null) return;

        var baseAngle = MathF.Atan2(directionToTarget.Value.Y, directionToTarget.Value.X);
        var spreadRadians = state.Attack.Spread * (MathF.PI / 180f);

        for (int i = 0; i < state.Attack.Shots; i++)
        {
            var angleOffset = (i - (state.Attack.Shots - 1) / 2f) * (spreadRadians / state.Attack.Shots);
            var shotAngle = baseAngle + angleOffset;
            var direction = new Vector2(MathF.Cos(shotAngle), MathF.Sin(shotAngle));
            SpawnProjectile(state.Owner, state.Attack, direction);
        }
    }

    private void ExecuteRapidFirePattern(NPCRangedState state)
    {
        var direction = GetDirectionToTarget(state) ?? Vector2.Zero;
        var random = new Random();
        var spreadAngle = random.Next((int)-state.Attack.Spread, (int)state.Attack.Spread);
        var angle = MathF.Atan2(direction.Y, direction.X) + spreadAngle * (MathF.PI / 180f);
        direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        SpawnProjectile(state.Owner, state.Attack, direction);
    }

    private void SpawnProjectile(EntityUid owner, NpcRangedAttackPatternComponent attack, Vector2 direction)
    {
        var coordinates = Transform(owner).Coordinates;
        var projectile = Spawn(attack.Projectile, coordinates);

        if (!TryComp<ProjectileComponent>(projectile, out var projectileComponent))
            return;

        _gunSystem.ShootProjectile(
            projectile,
            direction,
            Vector2.Zero,
            owner,
            owner,
            attack.Speed
        );
    }

    private Vector2[] GetCardinalDirections()
    {
        return new[]
        {
            new Vector2(0, 1),
            new Vector2(1, 0),
            new Vector2(0, -1),
            new Vector2(-1, 0)
        };
    }

    private Vector2[] GetDiagonalDirections()
    {
        return new[]
        {
            new Vector2(1, 1),
            new Vector2(1, -1),
            new Vector2(-1, -1),
            new Vector2(-1, 1)
        };
    }
}

public sealed class NPCRangedState
{
    public EntityUid Owner;
    public EntityUid AttackEntity;
    public NpcRangedAttackPatternComponent Attack = null!;
    public EntityUid? Target;
    public int CurrentShot = 0;
    public float CurrentAngle = 0f;
    public float NextShotTime = 0f;
    public string AttackId = "";
}
