using System.Threading;
using System.Threading.Tasks;
using Content.Server._KS14.NPC.Perception;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Perception;

/// <summary>
///     Works out how near the owner has to get to the hostile at <see cref="TargetKey"/> to notice it, and puts that at
///         <see cref="Key"/>, no more than <see cref="Max"/>: see <see cref="NpcPerceptionSystem.GetNoticeRange"/>. For a move
///         that has to end where the hostile can be seen, not just where there is a line to it, which in the dark is only
///         close up. A plan effect, worked out when planned: a pure read.
/// </summary>
public sealed partial class NoticeRangeOperator : HTNOperator
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private NpcPerceptionSystem _npcPerceptionSystem = default!;

    [DataField(required: true)]
    public string TargetKey = string.Empty;

    [DataField(required: true)]
    public string Key = string.Empty;

    /// <summary>
    ///     The furthest it is ever put at.
    /// </summary>
    [DataField]
    public float Max = float.MaxValue;

    /// <summary>
    ///     A blackboard float in place of <see cref="Max"/>, if the owner has it.
    /// </summary>
    [DataField]
    public string? MaxKey;

    /// <summary>
    ///     How far inside the notice range to put it, for one that stops right at the edge of it.
    /// </summary>
    [DataField]
    public float Margin = 0.25f;

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        if (!blackboard.TryGetEntity(TargetKey, _entityManager, out var targetUid))
            return (false, null);

        var range = MathF.Min(blackboard.GetFloatOr(MaxKey, Max, _entityManager), _npcPerceptionSystem.GetNoticeRange(blackboard.GetOwner(), targetUid) - Margin);
        return (true, new Dictionary<string, object> { { Key, range } });
    }
}
