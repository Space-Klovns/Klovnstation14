using Content.Server._KS14.NPC.Hands;

namespace Content.Server._KS14.NPC.Execution;

/// <summary>
///     On an NPC only while it is executing someone: what it needs to put things back as they were. See
///         <see cref="NpcExecutionSystem"/>.
/// </summary>
[RegisterComponent]
[Access(typeof(NpcExecutionSystem))]
public sealed partial class NpcExecutingComponent : Component
{
    [ViewVariables]
    public NpcTakenOut TakenOut;
}
