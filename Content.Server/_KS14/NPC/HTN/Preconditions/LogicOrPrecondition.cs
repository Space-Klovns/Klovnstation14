using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;

namespace Content.Server._KS14.NPC.HTN.Preconditions;

/// <summary>
///     Returns true (or false if inverted) only one or more of the specified
///         preconditions are met.
/// </summary>
public sealed partial class LogicOrPrecondition : HTNPrecondition
{
    [DataField] public bool Invert;
    [DataField(required: true)] public HTNPrecondition[] Conditions;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);

        foreach (var condition in Conditions)
            condition.Initialize(sysManager);
    }

    public override bool IsMet(NPCBlackboard blackboard)
    {
        foreach (var condition in Conditions)
        {
            if (!condition.IsMet(blackboard))
                continue;

            return !Invert;
        }

        return Invert;
    }
}
