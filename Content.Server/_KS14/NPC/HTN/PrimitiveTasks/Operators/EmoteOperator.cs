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
        var uid = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (Hidden)
            _emoteSystem.TryEmoteWithoutChat(uid, Id, networkedFilter: Filter.Pvs(uid));
        else
            _emoteSystem.TryEmoteWithChat(uid, Id, networkedFilter: Filter.Pvs(uid));

        return HTNOperatorStatus.Finished;
    }
}
