namespace Content.Server._KS14.NPC.Components;

/// <summary>
///     On an NPC with perception that has shot at something: how far off its aim is going for the dark right now, and
///         until when. Added the first time it shoots. See <c>NPCCombatSystem.Klovn.DarkAim.cs</c>.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class NpcDarkAimComponent : Component
{
    /// <summary>
    ///     How far off the aim goes, and which way: a share of the spread the darkness allowed when it was rolled. Zero
    ///         for a target seen well enough.
    /// </summary>
    [ViewVariables]
    public Angle Offset;

    /// <summary>
    ///     What the offset was rolled for: a new target gets one of its own straight away.
    /// </summary>
    [ViewVariables]
    public EntityUid? TargetUid;

    /// <summary>
    ///     When the offset is rolled again, from how dark the target looks then.
    /// </summary>
    [ViewVariables, AutoPausedField]
    public TimeSpan RollAt;
}
