namespace Content.Server._KS14.NPC.Execution;

/// <summary>
///     Marks an NPC that, with nothing left to fight, walks up to any hostile left down and executes it, rather than
///         shooting it from where it stands. See <see cref="NpcExecutionSystem"/>.
/// </summary>
[RegisterComponent]
public sealed partial class NpcExecutionerComponent : Component;
