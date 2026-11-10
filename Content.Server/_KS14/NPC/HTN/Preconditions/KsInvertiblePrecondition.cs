using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;

namespace Content.Server._KS14.NPC.HTN.Preconditions;

/// <summary>
///     A precondition with an <c>invert</c> flag. Subclasses answer the plain question in <see cref="IsMetRaw"/>,
///         and this flips the answer when <see cref="Invert"/> is set - on every path, early exits included.
/// </summary>
public abstract partial class KsInvertiblePrecondition : HTNPrecondition
{
    /// <summary>
    ///     Met when the plain condition is not met, and the other way round.
    /// </summary>
    [DataField]
    public bool Invert;

    public sealed override bool IsMet(NPCBlackboard blackboard)
    {
        return IsMetRaw(blackboard) != Invert;
    }

    /// <summary>
    ///     Whether the condition holds, before <see cref="Invert"/> is applied.
    /// </summary>
    protected abstract bool IsMetRaw(NPCBlackboard blackboard);
}
