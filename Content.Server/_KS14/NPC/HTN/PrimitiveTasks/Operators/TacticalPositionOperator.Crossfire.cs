using System.Numerics;
using Content.Server.NPC;
using Content.Server.NPC.Components;
using Content.Server.NPC.Pathfinding;
using Robust.Shared.Map;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators;

public sealed partial class TacticalPositionOperator
{
    /// <summary>
    ///     Works out which way, seen from the hostile at <see cref="CrossfireTargetKey"/>, the owner's squadmates are
    ///         shooting it from. Leaves nothing to score with, at no cost, for a squad that does not do this.
    /// </summary>
    private void GatherCrossfire(NPCBlackboard blackboard, EntityUid owner)
    {
        _plan.CrossfireDirections.Clear();
        _plan.Crossfire = 0f;

        if (CrossfireTargetKey is null ||
            !blackboard.TryGetValue<EntityUid>(CrossfireTargetKey, out var targetUid, _entityManager) ||
            _npcSquadSystem.GetTactics(owner) is not { Crossfire: > 0f } tactics)
            return;

        _plan.Crossfire = tactics.Crossfire;
        var targetMapCoordinates = _transformSystem.GetMapCoordinates(targetUid);
        _plan.CrossfireTargetPosition = targetMapCoordinates.Position;

        _plan.CrossfireSquadmates.Clear();
        _npcSquadSystem.GetSquadmates(owner, _plan.CrossfireSquadmates);

        foreach (var squadmateUid in _plan.CrossfireSquadmates)
        {
            if (!_entityManager.TryGetComponent<NPCRangedCombatComponent>(squadmateUid, out var rangedCombatComponent) ||
                rangedCombatComponent.Target != targetUid)
                continue;

            var squadmateMapCoordinates = _transformSystem.GetMapCoordinates(squadmateUid);
            var direction = squadmateMapCoordinates.Position - _plan.CrossfireTargetPosition;

            if (squadmateMapCoordinates.MapId == targetMapCoordinates.MapId && direction.LengthSquared() > 0f)
                _plan.CrossfireDirections.Add(Vector2.Normalize(direction));
        }
    }

    /// <summary>
    ///     How much crossfire likes a candidate at <paramref name="candidate"/>, from 0 to 1: 1 for a spot at least a
    ///         right angle round the hostile from every squadmate shooting it, falling off as it closes on the nearest of
    ///         them.
    /// </summary>
    private float GetCrossfireFactor(MapCoordinates candidate)
    {
        var direction = candidate.Position - _plan.CrossfireTargetPosition;
        if (direction.LengthSquared() <= 0f)
            return 1f - _plan.Crossfire;

        direction = Vector2.Normalize(direction);

        // The dot product of two directions: 1 together, 0 at a right angle, -1 opposite.
        var closest = -1f;
        foreach (var squadmateDirection in _plan.CrossfireDirections)
        {
            closest = MathF.Max(closest, Vector2.Dot(direction, squadmateDirection));
        }

        return 1f - _plan.Crossfire * Math.Clamp(closest, 0f, 1f);
    }
}
