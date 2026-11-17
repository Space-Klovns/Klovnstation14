using Content.Server._KS14.NPC.HTN;
using Content.Server.Chat.Systems;
using Robust.Shared.Prototypes;
using Content.Shared.Chat.Prototypes;
using Robust.Shared.Player;

namespace Content.Server.NPC.HTN.PrimitiveTasks.Operators;

public sealed partial class EmoteOperator : HTNOperator
{
    [Dependency] private ChatSystem _emoteSystem = default!;

    [DataField(required: true)] public ProtoId<EmotePrototype> Id;
    [DataField] public bool Hidden;

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var uid = blackboard.GetOwner();
        if (Hidden)
            _emoteSystem.TryEmoteWithoutChat(uid, Id, networkedFilter: Filter.Pvs(uid));
        else
            _emoteSystem.TryEmoteWithChat(uid, Id, networkedFilter: Filter.Pvs(uid));

        return HTNOperatorStatus.Finished;
    }
}
