namespace Content.Server._KS14.NPC.Internals;

/// <summary>
///     Has an NPC put its internals on when what it is about to breathe would hurt it, and take them off once it has been
///         breathing safe air a while. Needs a breath tool on (a mask) and a gas tank to hand. See
///         <see cref="NpcInternalsSystem"/>.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class NpcInternalsUserComponent : Component
{
    /// <summary>
    ///     How long internals stay on after the last bad breath, or the last smoke carrying something.
    /// </summary>
    [DataField]
    public TimeSpan HoldTime = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     Until when internals stay on, whatever the air is like.
    /// </summary>
    [DataField, AutoPausedField]
    public TimeSpan OnUntil;

    /// <summary>
    ///     Whether the internals on now were put on by this NPC. Only those does it take off: never ones a player or an admin
    ///         put on for it.
    /// </summary>
    [DataField]
    public bool SwitchedOn;
}
