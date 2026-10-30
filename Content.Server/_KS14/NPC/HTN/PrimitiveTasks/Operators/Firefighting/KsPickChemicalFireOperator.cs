using System.Threading;
using System.Threading.Tasks;
using Content.Server._KS14.NPC.Firefighting;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Firefighting;

/// <summary>
///     Picks the chemical fire tile the NPC should fight next, and how close it has to get to spray it. Fails when no
///         fire needs it. See <see cref="KsNpcFirefightingSystem.TryPickTarget"/>.
/// </summary>
public sealed partial class KsPickChemicalFireOperator : HTNOperator
{
    [Dependency] private KsNpcFirefightingSystem _firefightingSystem = default!;

    /// <summary>
    ///     Key the coordinates of the tile to go to are written to.
    /// </summary>
    [DataField]
    public string TargetKey = "TargetCoordinates";

    /// <summary>
    ///     Key the range to close to is written to.
    /// </summary>
    [DataField]
    public string RangeKey = "MovementRange";

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!_firefightingSystem.TryPickTarget(owner, out var targetCoordinates, out var approachRange))
            return (false, null);

        return (true, new Dictionary<string, object>
        {
            { TargetKey, targetCoordinates },
            { RangeKey, approachRange },
        });
    }
}
