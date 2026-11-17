namespace Content.Server._KS14.NPC.Burning;

/// <summary>
///     Changes how an NPC puts itself out. Any awake NPC that can do so does, with or without this: see
///         <see cref="NpcStopDropRollSystem"/>.
/// </summary>
[RegisterComponent]
public sealed partial class NpcStopDropRollComponent : Component
{
    /// <summary>
    ///     Whether it keeps hold of what is in its hands, rather than dropping it as anything else does. Otherwise a lighter
    ///         disarms it: set it alight, and pick up the gun.
    /// </summary>
    [DataField]
    public bool KeepsHold;

    /// <summary>
    ///     A blackboard key that, while it is there, means it has somewhere more urgent to be: a retreat, say.
    /// </summary>
    [DataField]
    public string? BusyKey;
}
