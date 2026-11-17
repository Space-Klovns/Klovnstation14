using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Robust.Shared.Map;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators;

/// <summary>
///     Sets the value of the target key to that of the origin key (which must be present).
///         Doesnt actually copy unless the copied data is by-value.
/// </summary>
public sealed partial class CopyKeyOperator : HTNOperator
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    [DataField(required: true)] public string OriginKey = "Origin";
    [DataField(required: true)] public string TargetKey = "Target";

    /// <summary>
    ///     If the value is <see cref="EntityCoordinates"/>, copy where they point right now, relative to the grid or
    ///         map, rather than the coordinates themselves. Coordinates relative to a mob - which is what
    ///         <c>UtilityOperator</c> writes for its target - follow the mob around; a snapshot stays where it was,
    ///         which is what "where it was last seen" means.
    /// </summary>
    [DataField] public bool Snapshot;

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken _)
    {
        var value = blackboard.GetValueOrDefault<object>(OriginKey, _entityManager)!;

        if (Snapshot && value is EntityCoordinates coordinates && !_entityManager.Deleted(coordinates.EntityId))
            value = _transformSystem.GetMoverCoordinates(coordinates);

        return (true, new Dictionary<string, object>
        {
            {TargetKey, value}
        });
    }
}
