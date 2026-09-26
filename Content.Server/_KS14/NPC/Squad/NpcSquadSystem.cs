using System.Diagnostics.CodeAnalysis;
using Content.Server.NPC.HTN;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._KS14.NPC.Squad;

/// <summary>
///     Groups NPCs with <see cref="NpcSquadMemberComponent"/> into squads with exactly one leader each.
///
///     Unassigned NPCs join the nearest friendly squad in range and line of sight that has room, or found their
///         own. Squads that drop below their assimilation threshold merge into a nearby one. When a leader dies,
///         the healthiest remaining member takes over; a squad with no members left is deleted.
/// </summary>
public sealed partial class NpcSquadSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private DamageableSystem _damageableSystem = default!;
    [Dependency] private EntityLookupSystem _entityLookupSystem = default!;
    [Dependency] private ExamineSystemShared _examineSystem = default!;
    [Dependency] private MetaDataSystem _metaDataSystem = default!;
    [Dependency] private MobStateSystem _mobStateSystem = default!;
    [Dependency] private MobThresholdSystem _mobThresholdSystem = default!;
    [Dependency] private NpcFactionSystem _npcFactionSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    [Dependency] private EntityQuery<NpcSquadComponent> _squadQuery = default!;
    [Dependency] private EntityQuery<NpcSquadMemberComponent> _squadMemberQuery = default!;
    [Dependency] private EntityQuery<HTNComponent> _htnQuery = default!;
    [Dependency] private EntityQuery<DamageableComponent> _damageableQuery = default!;
    [Dependency] private EntityQuery<NpcFactionMemberComponent> _factionMemberQuery = default!;

    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);

    private TimeSpan _nextUpdate;

    private readonly HashSet<Entity<NpcSquadMemberComponent>> _nearbyMembers = new();
    private readonly List<EntityUid> _scratchMembers = new();
    private readonly List<(EntityUid From, EntityUid Into)> _pendingMerges = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _gameTiming.CurTime;
        if (now < _nextUpdate)
            return;

        _nextUpdate = now + UpdateInterval;

        PruneMembers();
        AssignUnsquadded();
        AssimilateSmallSquads();
    }

    #region Public API

    public bool TryGetSquad(EntityUid memberUid, [NotNullWhen(true)] out Entity<NpcSquadComponent>? squadEntity)
    {
        squadEntity = null;

        if (!_squadMemberQuery.TryComp(memberUid, out var squadMemberComponent) ||
            squadMemberComponent.Squad is not { } squadUid ||
            !_squadQuery.TryComp(squadUid, out var squadComponent))
            return false;

        squadEntity = (squadUid, squadComponent);
        return true;
    }

    public bool IsLeader(EntityUid memberUid)
    {
        return TryGetSquad(memberUid, out var squadEntity) && squadEntity.Value.Comp.Leader == memberUid;
    }

    /// <summary>
    ///     Records <paramref name="threatCoordinates"/> as the latest known hostile position for
    ///         <paramref name="memberUid"/>'s whole squad.
    /// </summary>
    public void ReportThreat(EntityUid memberUid, EntityCoordinates threatCoordinates)
    {
        if (!TryGetSquad(memberUid, out var squadEntity))
            return;

        squadEntity.Value.Comp.ThreatCoordinates = threatCoordinates;
    }

    #endregion

    #region Events

    [SubscribeLocalEvent]
    private void OnMobStateChanged(Entity<NpcSquadMemberComponent> entity, ref MobStateChangedEvent args)
    {
        // Succession has to be immediate: a leaderless squad would otherwise hold for up to a whole update.
        if (args.NewMobState != MobState.Alive)
            RemoveFromSquad(entity);
    }

    [SubscribeLocalEvent]
    private void OnMemberShutdown(Entity<NpcSquadMemberComponent> entity, ref ComponentShutdown args)
    {
        RemoveFromSquad(entity);
    }

    [SubscribeLocalEvent]
    private void OnSquadShutdown(Entity<NpcSquadComponent> entity, ref ComponentShutdown args)
    {
        foreach (var memberUid in entity.Comp.Members)
        {
            if (_squadMemberQuery.TryComp(memberUid, out var squadMemberComponent) && squadMemberComponent.Squad == entity.Owner)
                squadMemberComponent.Squad = null;
        }

        entity.Comp.Members.Clear();
        entity.Comp.Leader = null;
    }

    #endregion

    #region Membership

    /// <summary>
    ///     Whether <paramref name="uid"/> should be in a squad at all right now.
    /// </summary>
    private bool IsEligible(EntityUid uid)
    {
        return !TerminatingOrDeleted(uid) &&
            _htnQuery.HasComp(uid) &&
            _mobStateSystem.IsAlive(uid);
    }

    /// <summary>
    ///     Catches everything that has no event to react to, such as an NPC losing its HTN component.
    /// </summary>
    private void PruneMembers()
    {
        _scratchMembers.Clear();

        var squadEnumerator = EntityQueryEnumerator<NpcSquadComponent>();
        while (squadEnumerator.MoveNext(out _, out var squadComponent))
        {
            foreach (var memberUid in squadComponent.Members)
            {
                if (!IsEligible(memberUid))
                    _scratchMembers.Add(memberUid);
            }
        }

        foreach (var memberUid in _scratchMembers)
        {
            if (_squadMemberQuery.TryComp(memberUid, out var squadMemberComponent))
                RemoveFromSquad((memberUid, squadMemberComponent));
        }
    }

    private void AssignUnsquadded()
    {
        var memberEnumerator = EntityQueryEnumerator<NpcSquadMemberComponent>();
        while (memberEnumerator.MoveNext(out var memberUid, out var squadMemberComponent))
        {
            if (squadMemberComponent.Squad is not null || !IsEligible(memberUid))
                continue;

            var memberEntity = new Entity<NpcSquadMemberComponent>(memberUid, squadMemberComponent);

            if (TryFindJoinableSquad(memberEntity, extraMembers: 1, excludedSquadUid: null, out var squadUid))
                AddToSquad(memberEntity, squadUid.Value);
            else
                FoundSquad(memberEntity);
        }
    }

    private void AssimilateSmallSquads()
    {
        _pendingMerges.Clear();

        var squadEnumerator = EntityQueryEnumerator<NpcSquadComponent>();
        while (squadEnumerator.MoveNext(out var squadUid, out var squadComponent))
        {
            if (squadComponent.Leader is not { } leaderUid ||
                !_squadMemberQuery.TryComp(leaderUid, out var leaderSquadMemberComponent) ||
                squadComponent.Members.Count >= leaderSquadMemberComponent.AssimilationThreshold)
                continue;

            EntityUid? bestSquadUid = null;
            var bestDistance = float.MaxValue;

            foreach (var memberUid in squadComponent.Members)
            {
                if (!_squadMemberQuery.TryComp(memberUid, out var squadMemberComponent))
                    continue;

                if (!TryFindJoinableSquad((memberUid, squadMemberComponent), squadComponent.Members.Count, squadUid, out var candidateSquadUid, out var distance) ||
                    distance >= bestDistance)
                    continue;

                // Only ever merge the smaller squad into the larger one, and break ties by uid, so two small
                //      squads next to each other do not both try to merge into the other.
                var candidateCount = _squadQuery.Comp(candidateSquadUid.Value).Members.Count;
                if (candidateCount < squadComponent.Members.Count ||
                    candidateCount == squadComponent.Members.Count && candidateSquadUid.Value.Id > squadUid.Id)
                    continue;

                bestSquadUid = candidateSquadUid;
                bestDistance = distance;
            }

            if (bestSquadUid is { } intoUid)
                _pendingMerges.Add((squadUid, intoUid));
        }

        foreach (var (fromUid, intoUid) in _pendingMerges)
        {
            // An earlier merge this update may have emptied or filled either side.
            if (!_squadQuery.TryComp(fromUid, out var fromSquadComponent) ||
                !_squadQuery.TryComp(intoUid, out var intoSquadComponent) ||
                fromSquadComponent.Members.Count == 0 ||
                intoSquadComponent.Members.Count + fromSquadComponent.Members.Count > GetMaxSize(intoSquadComponent))
                continue;

            _scratchMembers.Clear();
            _scratchMembers.AddRange(fromSquadComponent.Members);

            foreach (var memberUid in _scratchMembers)
            {
                if (!_squadMemberQuery.TryComp(memberUid, out var squadMemberComponent))
                    continue;

                RemoveFromSquad((memberUid, squadMemberComponent));
                AddToSquad((memberUid, squadMemberComponent), intoUid);
            }
        }
    }

    private bool TryFindJoinableSquad(
        Entity<NpcSquadMemberComponent> memberEntity,
        int extraMembers,
        EntityUid? excludedSquadUid,
        [NotNullWhen(true)] out EntityUid? squadUid)
    {
        return TryFindJoinableSquad(memberEntity, extraMembers, excludedSquadUid, out squadUid, out _);
    }

    /// <summary>
    ///     Finds the squad with a member nearest to <paramref name="memberEntity"/> that is in range, in line of
    ///         sight, friendly, and has room for <paramref name="extraMembers"/> more.
    /// </summary>
    private bool TryFindJoinableSquad(
        Entity<NpcSquadMemberComponent> memberEntity,
        int extraMembers,
        EntityUid? excludedSquadUid,
        [NotNullWhen(true)] out EntityUid? squadUid,
        out float distance)
    {
        squadUid = null;
        distance = float.MaxValue;

        var memberCoordinates = _transformSystem.GetMapCoordinates(memberEntity.Owner);

        _nearbyMembers.Clear();
        _entityLookupSystem.GetEntitiesInRange(memberCoordinates, memberEntity.Comp.JoinRange, _nearbyMembers);

        foreach (var otherEntity in _nearbyMembers)
        {
            if (otherEntity.Owner == memberEntity.Owner ||
                otherEntity.Comp.Squad is not { } otherSquadUid ||
                otherSquadUid == excludedSquadUid ||
                !_squadQuery.TryComp(otherSquadUid, out var otherSquadComponent) ||
                otherSquadComponent.Members.Count + extraMembers > GetMaxSize(otherSquadComponent) ||
                !IsFriendly(memberEntity.Owner, otherEntity.Owner))
                continue;

            var otherCoordinates = _transformSystem.GetMapCoordinates(otherEntity.Owner);
            var otherDistance = (otherCoordinates.Position - memberCoordinates.Position).Length();

            if (otherDistance >= distance ||
                !_examineSystem.InRangeUnOccluded(memberEntity.Owner, otherEntity.Owner, memberEntity.Comp.JoinRange))
                continue;

            squadUid = otherSquadUid;
            distance = otherDistance;
        }

        return squadUid is not null;
    }

    private bool IsFriendly(EntityUid uid, EntityUid otherUid)
    {
        // NPCs with no faction at all only ever squad up with each other.
        var hasFaction = _factionMemberQuery.TryComp(uid, out var factionMemberComponent);
        var otherHasFaction = _factionMemberQuery.TryComp(otherUid, out var otherFactionMemberComponent);

        if (!hasFaction || !otherHasFaction)
            return hasFaction == otherHasFaction;

        // IsEntityFriendly is one-way; a squad needs both sides to agree.
        return _npcFactionSystem.IsEntityFriendly((uid, factionMemberComponent), (otherUid, otherFactionMemberComponent)) &&
            _npcFactionSystem.IsEntityFriendly((otherUid, otherFactionMemberComponent), (uid, factionMemberComponent));
    }

    /// <summary>
    ///     A squad's size cap is its leader's, so it stays put however members come and go.
    /// </summary>
    private int GetMaxSize(NpcSquadComponent squadComponent)
    {
        if (squadComponent.Leader is { } leaderUid && _squadMemberQuery.TryComp(leaderUid, out var leaderSquadMemberComponent))
            return leaderSquadMemberComponent.MaxSquadSize;

        return 0;
    }

    private void FoundSquad(Entity<NpcSquadMemberComponent> founderEntity)
    {
        var squadUid = Spawn(null, MapCoordinates.Nullspace);
        _metaDataSystem.SetEntityName(squadUid, $"squad ({ToPrettyString(founderEntity.Owner)})");
        AddComp<NpcSquadComponent>(squadUid);

        AddToSquad(founderEntity, squadUid);
    }

    private void AddToSquad(Entity<NpcSquadMemberComponent> memberEntity, EntityUid squadUid)
    {
        var squadComponent = _squadQuery.Comp(squadUid);

        memberEntity.Comp.Squad = squadUid;
        squadComponent.Members.Add(memberEntity.Owner);
        squadComponent.Leader ??= memberEntity.Owner;
        squadComponent.Revision++;
    }

    private void RemoveFromSquad(Entity<NpcSquadMemberComponent> memberEntity)
    {
        if (memberEntity.Comp.Squad is not { } squadUid)
            return;

        memberEntity.Comp.Squad = null;

        if (!_squadQuery.TryComp(squadUid, out var squadComponent))
            return;

        squadComponent.Members.Remove(memberEntity.Owner);
        squadComponent.Revision++;

        if (squadComponent.Members.Count == 0)
        {
            squadComponent.Leader = null;
            QueueDel(squadUid);
            return;
        }

        if (squadComponent.Leader == memberEntity.Owner)
            squadComponent.Leader = GetHealthiest(squadComponent.Members);
    }

    /// <summary>
    ///     The member with the most health left as a fraction of its critical threshold, so NPCs with
    ///         different thresholds compare fairly.
    /// </summary>
    private EntityUid GetHealthiest(List<EntityUid> memberUids)
    {
        var bestUid = memberUids[0];
        var bestHealth = float.MinValue;

        foreach (var memberUid in memberUids)
        {
            var health = GetHealthFraction(memberUid);
            if (health <= bestHealth)
                continue;

            bestUid = memberUid;
            bestHealth = health;
        }

        return bestUid;
    }

    private float GetHealthFraction(EntityUid uid)
    {
        if (!_damageableQuery.TryComp(uid, out var damageableComponent))
            return 1f;

        var totalDamage = _damageableSystem.GetTotalDamage((uid, damageableComponent));

        if (!_mobThresholdSystem.TryGetThresholdForState(uid, MobState.Critical, out var criticalThreshold) ||
            criticalThreshold.Value <= FixedPoint2.Zero)
            return -(float)totalDamage;

        return 1f - (float)(totalDamage / criticalThreshold.Value);
    }

    #endregion
}
