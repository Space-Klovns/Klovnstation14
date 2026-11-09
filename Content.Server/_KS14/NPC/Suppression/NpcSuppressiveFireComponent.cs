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
    ///     How far to either side of <see cref="Coordinates"/>, in tiles, it may fire instead, to keep its shots off a friend.
    /// </summary>
    [ViewVariables]
    public float Spread;
}
