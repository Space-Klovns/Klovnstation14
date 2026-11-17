namespace Content.Server._KS14.NPC.Recovery;

/// <summary>
///     On an NPC only while it is waiting for its meds to work. See <see cref="NpcRecoverySystem"/>.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
[Access(typeof(NpcRecoverySystem))]
public sealed partial class NpcRecoveringComponent : Component
{
    /// <summary>
    ///     When it gives up waiting.
    /// </summary>
    [ViewVariables, AutoPausedField]
    public TimeSpan EndTime;
}
