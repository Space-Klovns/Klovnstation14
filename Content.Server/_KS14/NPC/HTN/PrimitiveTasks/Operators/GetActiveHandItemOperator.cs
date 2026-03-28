using System.Threading;
using System.Threading.Tasks;
using Content.Server.Hands.Systems;
using Content.Server.NPC;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators;

/// <summary>
///     Sets a key to the uid of the thing in the active hand.
/// </summary>
public sealed partial class GetActiveHandItemOperator : HTNOperator
{
    [Dependency] private readonly HandsSystem _handsSystem = default!;

    /// <summary>
    ///     Key that will contain the entity.
    /// </summary>
    [DataField(required: true)] public string Key;

    public override Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken _) => (true, new Dictionary<string, object>()
        {
            {Key, NPCBlackboard.ActiveHand}
        });
}
