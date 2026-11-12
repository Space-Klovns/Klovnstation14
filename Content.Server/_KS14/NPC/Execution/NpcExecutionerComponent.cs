namespace Content.Server._KS14.NPC.Execution;

/// <summary>
///     Marks an NPC that, with nothing left to fight, walks up to any hostile left down and executes it, rather than
///         shooting it from where it stands. See <see cref="NpcExecutionSystem"/>.
/// </summary>
/// <remarks>
///     An execution that is cancelled once - the victim crawling off, dragged or thrown, the executioner shoved or
///         stunned, anything that breaks the do-after - is not tried again on that victim: it is shot instead.
/// </remarks>
[RegisterComponent]
public sealed partial class NpcExecutionerComponent : Component;
