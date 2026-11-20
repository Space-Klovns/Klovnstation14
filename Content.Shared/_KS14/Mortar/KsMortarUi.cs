using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Mortar;

[Serializable, NetSerializable]
public enum KsMortarUiKey : byte
{
    Key,
}

/// <summary>
///     What the mortar's window shows: raw values, for the window to format. Sent when it opens and after every shot.
/// </summary>
[Serializable, NetSerializable]
public sealed class KsMortarBuiState(int rounds, float minRange, float maxRange, TimeSpan readyAt) : BoundUserInterfaceState
{
    public int Rounds = rounds;
    public float MinRange = minRange;
    public float MaxRange = maxRange;

    /// <summary>
    ///     When it can fire again, on the server's clock.
    /// </summary>
    public TimeSpan ReadyAt = readyAt;
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
