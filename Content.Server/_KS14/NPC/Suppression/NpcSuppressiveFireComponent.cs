using Robust.Shared.Map;

namespace Content.Server._KS14.NPC.Suppression;

/// <summary>
///     On an NPC only while it is firing at a spot. See <see cref="NpcSuppressiveFireSystem"/>.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
[Access(typeof(NpcSuppressiveFireSystem))]
public sealed partial class NpcSuppressiveFireComponent : Component
{
    /// <summary>
    ///     Where it is firing. Belongs to its grid, so it stays put if the grid moves.
    /// </summary>
    [ViewVariables]
    public EntityCoordinates Coordinates;

    [ViewVariables, AutoPausedField]
    public TimeSpan EndTime;

    /// <summary>
    ///     The hostile it is keeping down, if it is one: where that could be by now is as good to fire at as the spot itself,
    ///         and a round there clear of a friend does as well. Null for a spot that has to be hit as it is, such as a window.
    /// </summary>
    [ViewVariables]
    public EntityUid? TargetUid;
}
