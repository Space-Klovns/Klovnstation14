using System.Numerics;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Mortar;

[Serializable, NetSerializable]
public enum KsMortarUiKey : byte
{
    Key,
}

/// <summary>
///     What the mortar's window shows: raw values, for the window to format. Sent when it opens, after every shot and when a
///         shell lands.
/// </summary>
[Serializable, NetSerializable]
public sealed class KsMortarBuiState(
    int rounds,
    float minRange,
    float maxRange,
    float spread,
    TimeSpan readyAt,
    TimeSpan reloadTime,
    List<Vector2> impacts) : BoundUserInterfaceState
{
    public int Rounds = rounds;
    public float MinRange = minRange;
    public float MaxRange = maxRange;

    /// <summary>
    ///     How far from its aim, at most, a shell lands, in tiles.
    /// </summary>
    public float Spread = spread;

    /// <summary>
    ///     When it can fire again, on the server's clock.
    /// </summary>
    public TimeSpan ReadyAt = readyAt;

    /// <summary>
    ///     How long it takes to be ready again after a shot: what <see cref="ReadyAt"/> counts down from.
    /// </summary>
    public TimeSpan ReloadTime = reloadTime;

    /// <summary>
    ///     Where its last few shells landed, relative to the mortar, on the map's axes (+Y north), oldest first.
    /// </summary>
    public List<Vector2> Impacts = impacts;
}

/// <summary>
///     Fire at <paramref name="Bearing"/> (a compass bearing: zero is north, turning clockwise) and
///         <paramref name="Distance"/> tiles. The server clamps the distance to the mortar's range and refuses what is not
///         a number.
/// </summary>
[Serializable, NetSerializable]
public sealed class KsMortarFireMessage(Angle bearing, float distance) : BoundUserInterfaceMessage
{
    public Angle Bearing = bearing;
    public float Distance = distance;
}
