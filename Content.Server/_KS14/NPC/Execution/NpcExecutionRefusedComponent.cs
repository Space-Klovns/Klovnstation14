namespace Content.Server._KS14.NPC.Execution;

/// <summary>
///     On a hostile whose execution was cancelled, however it came about: executioners do not try again, and it is shot
///         like anything else that is down. Kept on the victim so that the whole squad switches together. See
///         <see cref="NpcExecutionSystem"/>.
/// </summary>
[RegisterComponent]
[Access(typeof(NpcExecutionSystem))]
public sealed partial class NpcExecutionRefusedComponent : Component;
