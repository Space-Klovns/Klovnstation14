using Content.Server._KS14.NPC.Grenades;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Robust.Shared.Map;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Grenades;

/// <summary>
///     Throws the grenade at <see cref="GrenadeKey"/> at the coordinates at <see cref="CoordinatesKey"/>. See
///         <see cref="NpcGrenadeSystem"/>.
/// </summary>
public sealed partial class ThrowGrenadeOperator : HTNOperator
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private NpcGrenadeSystem _npcGrenadeSystem = default!;

    [DataField]
    public string GrenadeKey = "ChosenGrenade";

    [DataField]
    public string CoordinatesKey = "GrenadeThrowCoordinates";

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        if (!blackboard.TryGetValue<EntityUid>(GrenadeKey, out var grenadeUid, _entityManager) ||
            !blackboard.TryGetValue<EntityCoordinates>(CoordinatesKey, out var coordinates, _entityManager))
            return HTNOperatorStatus.Failed;

        return _npcGrenadeSystem.TryThrow(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner), grenadeUid, coordinates)
            ? HTNOperatorStatus.Finished
            : HTNOperatorStatus.Failed;
    }
}
