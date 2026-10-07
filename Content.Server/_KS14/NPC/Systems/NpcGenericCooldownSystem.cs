using Content.Server._KS14.NPC.Components;
using Robust.Shared.Timing;

namespace Content.Server._KS14.NPC.Systems;

/// <summary>
///     Named cooldowns on an NPC, for HTN to set (<c>CooldownOperator</c>) and check (<c>CooldownPrecondition</c>).
///         Nothing ticks them: a key is on cooldown while its end time is still to come, and ended ones are dropped when
///         the next is set.
/// </summary>
public sealed partial class NpcGenericCooldownSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private EntityQuery<NpcGenericCooldownComponent> _cooldownQuery = default!;

    public void SetCooldown(EntityUid uid, string stringKey, TimeSpan endTime)
    {
        SetCooldown(uid, stringKey.GetHashCode(), endTime);
    }

    public void SetCooldown(EntityUid uid, int stringKeyHash, TimeSpan endTime)
    {
        var cooldownComponent = EnsureComp<NpcGenericCooldownComponent>(uid);
        var now = _gameTiming.CurTime;

        // Removing while enumerating is allowed for a Dictionary.
        foreach (var (keyHash, keyEndTime) in cooldownComponent.CooldownEndTimes)
        {
            if (keyEndTime <= now)
                cooldownComponent.CooldownEndTimes.Remove(keyHash);
        }

        cooldownComponent.CooldownEndTimes[stringKeyHash] = endTime;
    }

    public bool IsKeyOnCooldown(Entity<NpcGenericCooldownComponent?> entity, string stringKey)
    {
        return IsKeyOnCooldown(entity, stringKey.GetHashCode());
    }

    public bool IsKeyOnCooldown(Entity<NpcGenericCooldownComponent?> entity, int stringKeyHash)
    {
        return _cooldownQuery.Resolve(entity.Owner, ref entity.Comp, logMissing: false) &&
            entity.Comp.CooldownEndTimes.TryGetValue(stringKeyHash, out var cooldownEndTime) &&
            _gameTiming.CurTime < cooldownEndTime;
    }
}
