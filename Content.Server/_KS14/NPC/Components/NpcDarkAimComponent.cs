namespace Content.Server._KS14.NPC.Components;

/// <summary>
///     On an NPC that has shot in the dark: how far off its aim is going right now, and until when. Added the first time
///         it needs one. See <c>NPCCombatSystem.Klovn.DarkAim.cs</c>.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class NpcDarkAimComponent : Component
{
    /// <summary>
    ///     Which way and how far off the aim goes, from -1 to 1 of the spread the darkness allows.
    /// </summary>
    [ViewVariables]
    public float Error;

    [ViewVariables, AutoPausedField]
    public TimeSpan RollAt;
}
