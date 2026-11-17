using Robust.Shared.Map;

namespace Content.Server._KS14.NPC.Grenades;

/// <summary>
///     On an NPC from a grenade going off until it gets there or <see cref="EndTime"/>, checked when read so nothing ticks
///         it. See <see cref="NpcGrenadeSystem"/>.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
[Access(typeof(NpcGrenadeSystem))]
public sealed partial class NpcGrenadePushComponent : Component
{
    /// <summary>
    ///     Where to go. Belongs to its grid, so it stays put if the grid moves.
    /// </summary>
    [ViewVariables]
    public EntityCoordinates Coordinates;

    [ViewVariables, AutoPausedField]
    public TimeSpan EndTime;
}
