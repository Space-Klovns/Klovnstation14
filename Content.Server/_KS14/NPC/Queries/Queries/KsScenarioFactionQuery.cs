using Content.Server.NPC;
using Content.Server.NPC.Queries.Queries;
using Content.Shared._KS14.Scenario.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.NPC.Queries.Queries;

/// <summary>
///     Scenario entities on the owner's map, at any distance unless <see cref="Range"/> says otherwise, belonging to
///         the factions it is told to look for. Unlike a <c>ComponentQuery</c>, it is not bounded by the owner's vision,
///         and it reads the faction each entity belongs to rather than only whether it has the component.
/// </summary>
public abstract partial class KsScenarioFactionQuery : UtilityQuery
{
    [Dependency] protected IEntityManager EntityManager = default!;
    [Dependency] protected TransformSystem TransformSystem = default!;

    /// <summary>
    ///     Only these factions. Empty for any.
    /// </summary>
    [DataField]
    public HashSet<ProtoId<ScenarioFactionPrototype>> Factions = [];

    /// <summary>
    ///     Leave out the owner's own faction, if it is in one.
    /// </summary>
    [DataField]
    public bool ExcludeOwnFaction = true;

    /// <summary>
    ///     How far from the owner to look, or the whole map.
    /// </summary>
    [DataField]
    public float? Range;

    public override void AddEntities(NPCBlackboard blackboard, EntityUid ownerUid, HashSet<EntityUid> entities)
    {
        var ownerCoordinates = TransformSystem.GetMapCoordinates(ownerUid);
        if (ownerCoordinates.MapId == MapId.Nullspace)
            return;

        ProtoId<ScenarioFactionPrototype>? ownFaction = null;
        if (ExcludeOwnFaction && EntityManager.TryGetComponent<ScenarioFactionMemberComponent>(ownerUid, out var ownerMemberComponent))
            ownFaction = ownerMemberComponent.Id;

        AddCandidates(ownerUid, ownerCoordinates, ownFaction, entities);
    }

    /// <summary>
    ///     Adds every candidate that <see cref="IsWanted"/>.
    /// </summary>
    protected abstract void AddCandidates(EntityUid ownerUid,
        MapCoordinates ownerCoordinates,
        ProtoId<ScenarioFactionPrototype>? ownFaction,
        HashSet<EntityUid> entities);

    /// <summary>
    ///     Whether a candidate of <paramref name="faction"/> at <paramref name="candidateTransform"/> is one to add.
    /// </summary>
    protected bool IsWanted(ProtoId<ScenarioFactionPrototype> faction,
        ProtoId<ScenarioFactionPrototype>? ownFaction,
        MapCoordinates ownerCoordinates,
        TransformComponent candidateTransform)
    {
        // The cheap answers first: most candidates are on the map and of a faction it cares about.
        if (candidateTransform.MapID != ownerCoordinates.MapId ||
            faction == ownFaction ||
            Factions.Count > 0 && !Factions.Contains(faction))
            return false;

        if (Range is not { } range)
            return true;

        var candidatePosition = TransformSystem.GetWorldPosition(candidateTransform);
        return (candidatePosition - ownerCoordinates.Position).LengthSquared() <= range * range;
    }
}

/// <summary>
///     Every scenario objective still standing on the owner's map, of the factions asked for: those an objective
///         capper should head for, wherever it landed.
/// </summary>
public sealed partial class KsScenarioObjectiveQuery : KsScenarioFactionQuery
{
    protected override void AddCandidates(EntityUid ownerUid,
        MapCoordinates ownerCoordinates,
        ProtoId<ScenarioFactionPrototype>? ownFaction,
        HashSet<EntityUid> entities)
    {
        var query = EntityManager.EntityQueryEnumerator<ScenarioObjectiveComponent, TransformComponent>();
        while (query.MoveNext(out var objectiveUid, out var objectiveComponent, out var transformComponent))
        {
            // One being removed, as a lost objective is, no longer stands.
            if (objectiveComponent.LifeStage > ComponentLifeStage.Running ||
                !IsWanted(objectiveComponent.FactionId, ownFaction, ownerCoordinates, transformComponent))
                continue;

            entities.Add(objectiveUid);
        }
    }
}

/// <summary>
///     Every member of the factions asked for on the owner's map, dead ones included (narrow with
///         <c>TargetIsAliveCon</c>), and never the owner.
/// </summary>
public sealed partial class KsScenarioFactionMemberQuery : KsScenarioFactionQuery
{
    protected override void AddCandidates(EntityUid ownerUid,
        MapCoordinates ownerCoordinates,
        ProtoId<ScenarioFactionPrototype>? ownFaction,
        HashSet<EntityUid> entities)
    {
        var query = EntityManager.EntityQueryEnumerator<ScenarioFactionMemberComponent, TransformComponent>();
        while (query.MoveNext(out var memberUid, out var memberComponent, out var transformComponent))
        {
            if (memberUid == ownerUid ||
                !IsWanted(memberComponent.Id, ownFaction, ownerCoordinates, transformComponent))
                continue;

            entities.Add(memberUid);
        }
    }
}
