using Content.Server._KS14.NPC.Utility;
using Content.Server.NPC.Components;
using Content.Shared.NPC.Components;
using Robust.Shared.Timing;

namespace Content.Server._KS14.NPC.Targeting;

/// <summary>
///     Counts, every <see cref="UpdateInterval"/>, how many NPCs of each faction are fighting each target - shooting it
///         (<see cref="NPCRangedCombatComponent"/>) or hitting it (<see cref="NPCMeleeCombatComponent"/>) - and keeps
///         the count on the target (<see cref="NpcTargetedComponent"/>), so that target queries can spread out
///         (<see cref="TargetCrowdedCon"/>) without each candidate walking every NPC.
/// </summary>
public sealed partial class NpcTargetTrackingSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;

    [Dependency] private EntityQuery<NpcFactionMemberComponent> _factionMemberQuery = default!;
    [Dependency] private EntityQuery<NpcTargetedComponent> _targetedQuery = default!;

    public static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(0.5);

    private TimeSpan _nextUpdate;
    private readonly List<EntityUid> _untargeted = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (NpcThrottle.TryConsume(ref _nextUpdate, _gameTiming.CurTime, UpdateInterval))
            UpdateNow();
    }

    /// <summary>
    ///     Counts again now, whatever the throttle says.
    /// </summary>
    public void UpdateNow()
    {
        var targetedQuery = EntityQueryEnumerator<NpcTargetedComponent>();
        while (targetedQuery.MoveNext(out var targetedComponent))
        {
            targetedComponent.Engaging.Clear();
            targetedComponent.Engagers.Clear();
        }

        var rangedQuery = EntityQueryEnumerator<NPCRangedCombatComponent, NpcFactionMemberComponent>();
        while (rangedQuery.MoveNext(out var npcUid, out var rangedCombatComponent, out var factionMemberComponent))
            Count((npcUid, factionMemberComponent), rangedCombatComponent.Target);

        var meleeQuery = EntityQueryEnumerator<NPCMeleeCombatComponent, NpcFactionMemberComponent>();
        while (meleeQuery.MoveNext(out var npcUid, out var meleeCombatComponent, out var factionMemberComponent))
            Count((npcUid, factionMemberComponent), meleeCombatComponent.Target);

        _untargeted.Clear();
        targetedQuery = EntityQueryEnumerator<NpcTargetedComponent>();
        while (targetedQuery.MoveNext(out var targetUid, out var targetedComponent))
        {
            if (targetedComponent.Engagers.Count == 0)
                _untargeted.Add(targetUid);
        }

        foreach (var targetUid in _untargeted)
            RemComp<NpcTargetedComponent>(targetUid);
    }

    private void Count(Entity<NpcFactionMemberComponent> npc, EntityUid targetUid)
    {
        if (!targetUid.IsValid() || TerminatingOrDeleted(targetUid))
            return;

        var targetedComponent = EnsureComp<NpcTargetedComponent>(targetUid);

        // Shooting and hitting the same target at once is still one NPC.
        if (!targetedComponent.Engagers.Add(npc.Owner))
            return;

        foreach (var faction in npc.Comp.Factions)
            targetedComponent.Engaging[faction] = targetedComponent.Engaging.GetValueOrDefault(faction) + 1;
    }

    /// <summary>
    ///     How many NPCs of <paramref name="ownerUid"/>'s factions were fighting <paramref name="targetUid"/> at the
    ///         last count, itself left out: the most of any one of its factions.
    /// </summary>
    public int GetEngagingAllies(EntityUid ownerUid, EntityUid targetUid)
    {
        if (!_targetedQuery.TryComp(targetUid, out var targetedComponent) ||
            !_factionMemberQuery.TryComp(ownerUid, out var factionMemberComponent))
            return 0;

        var count = 0;
        foreach (var faction in factionMemberComponent.Factions)
            count = Math.Max(count, targetedComponent.Engaging.GetValueOrDefault(faction));

        if (targetedComponent.Engagers.Contains(ownerUid))
            count--;

        return Math.Max(count, 0);
    }
}
