using Content.Server._KS14.NPC.Utility;
using Content.Server.NPC.Components;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Prototypes;
using Content.Shared.NPC.Systems;
using Robust.Shared.Prototypes;
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
    [Dependency] private NpcFactionSystem _npcFactionSystem = default!;

    [Dependency] private EntityQuery<NpcFactionMemberComponent> _factionMemberQuery = default!;
    [Dependency] private EntityQuery<NpcTargetedComponent> _targetedQuery = default!;
    [Dependency] private EntityQuery<NPCRangedCombatComponent> _rangedCombatQuery = default!;
    [Dependency] private EntityQuery<NPCMeleeCombatComponent> _meleeCombatQuery = default!;

    public static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(0.5);

    private TimeSpan _nextUpdate;
    private readonly List<EntityUid> _untargeted = new();
    private readonly Dictionary<ProtoId<NpcFactionPrototype>, int> _aheadCounts = new();

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
    ///         last count, itself left out: the most of any one of its factions. On the target the owner is fighting now,
    ///         only those ranked ahead of it count (a lower uid: any fixed order would do), so that of too many on one
    ///         target the last ranked move off and the rest keep it. Counting everyone else instead, every one of them
    ///         sees the same crowd, and all of them leave for the next target together, then all come back.
    /// </summary>
    public int GetEngagingAllies(EntityUid ownerUid, EntityUid targetUid)
    {
        if (!_targetedQuery.TryComp(targetUid, out var targetedComponent) ||
            !_factionMemberQuery.TryComp(ownerUid, out var factionMemberComponent))
            return 0;

        if (GetCurrentTarget(ownerUid) == targetUid)
            return CountAhead(ownerUid, factionMemberComponent, targetedComponent);

        var count = 0;
        foreach (var faction in factionMemberComponent.Factions)
            count = Math.Max(count, targetedComponent.Engaging.GetValueOrDefault(faction));

        // Counted on it last time, and on something else since.
        if (targetedComponent.Engagers.Contains(ownerUid))
            count--;

        return Math.Max(count, 0);
    }

    /// <summary>
    ///     What the owner is shooting or hitting now, if anything.
    /// </summary>
    private EntityUid? GetCurrentTarget(EntityUid ownerUid)
    {
        if (_rangedCombatQuery.TryComp(ownerUid, out var rangedCombatComponent) && rangedCombatComponent.Target.IsValid())
            return rangedCombatComponent.Target;

        if (_meleeCombatQuery.TryComp(ownerUid, out var meleeCombatComponent) && meleeCombatComponent.Target.IsValid())
            return meleeCombatComponent.Target;

        return null;
    }

    private int CountAhead(EntityUid ownerUid, NpcFactionMemberComponent factionMemberComponent, NpcTargetedComponent targetedComponent)
    {
        _aheadCounts.Clear();
        foreach (var engagerUid in targetedComponent.Engagers)
        {
            if (engagerUid.CompareTo(ownerUid) >= 0 || !_factionMemberQuery.TryComp(engagerUid, out var engagerFactions))
                continue;

            foreach (var faction in factionMemberComponent.Factions)
            {
                if (_npcFactionSystem.IsMember((engagerUid, engagerFactions), faction))
                    _aheadCounts[faction] = _aheadCounts.GetValueOrDefault(faction) + 1;
            }
        }

        var count = 0;
        foreach (var ahead in _aheadCounts.Values)
            count = Math.Max(count, ahead);

        return count;
    }
}
