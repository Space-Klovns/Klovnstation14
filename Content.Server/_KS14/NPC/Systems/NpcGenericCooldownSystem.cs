using Content.Server._KS14.NPC.Components;
using Robust.Shared.Timing;

namespace Content.Server._KS14.NPC.Systems;

public sealed partial class NpcGenericCooldownSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private EntityQuery<NpcGenericCooldownComponent> _cooldownQuery = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var eqe = EntityQueryEnumerator<NpcActiveGenericCooldownComponent, NpcGenericCooldownComponent>();
        var curTime = _gameTiming.CurTime;

        while (eqe.MoveNext(out var uid, out var activeComponent, out var cooldownComponent))
        {
            foreach (var (id, endTime) in cooldownComponent.CooldownEndTimes)
            {
                if (curTime < endTime)
                    continue;

                cooldownComponent.CooldownEndTimes.Remove(id);
            }

            if (cooldownComponent.CooldownEndTimes.Count == 0)
                RemComp(uid, activeComponent);
        }
    }

    public void SetCooldown(Entity<NpcGenericCooldownComponent?> entity, string stringKey, TimeSpan endTime)
        => SetCooldown(entity, stringKey.GetHashCode(), endTime);

    public void SetCooldown(Entity<NpcGenericCooldownComponent?> entity, int stringKeyHash, TimeSpan endTime)
    {
        if (!_cooldownQuery.TryGetComponent(entity, out var genericCooldownComponent))
            genericCooldownComponent = EnsureComp<NpcGenericCooldownComponent>(entity);

        genericCooldownComponent.CooldownEndTimes[stringKeyHash] = endTime;
        EnsureComp<NpcActiveGenericCooldownComponent>(entity);
    }

    public bool IsKeyOnCooldown(Entity<NpcGenericCooldownComponent?> entity, string stringKey)
        => IsKeyOnCooldown(entity, stringKey.GetHashCode());

    public bool IsKeyOnCooldown(Entity<NpcGenericCooldownComponent?> entity, int stringKeyHash)
    {
        if (!_cooldownQuery.Resolve(ref entity, logMissing: false) ||
            !entity.Comp!.CooldownEndTimes.TryGetValue(stringKeyHash, out var cooldownEndTime))
            return false;

        return _gameTiming.CurTime < cooldownEndTime;
    }
}
