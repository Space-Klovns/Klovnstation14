using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.NPC;

/// <summary>
///     Sent server -> the toggling client only, telling it whether it now receives <see cref="LineOfFireDebugDataMessage"/>s.
/// </summary>
[Serializable, NetSerializable]
public sealed class LineOfFireDebugStateMessage : EntityEventArgs
{
    public bool Enabled;
}

/// <summary>
///     Every NPC being watched that is shooting at something, and how far its shot would get.
/// </summary>
[Serializable, NetSerializable]
public sealed class LineOfFireDebugDataMessage : EntityEventArgs
{
    public readonly List<LineOfFireDebugShot> Shots = new();
}

/// <param name="From">The NPC.</param>
/// <param name="To">How far its shot gets: its target, or whatever stops it.</param>
/// <param name="Clear">Whether it gets all the way there.</param>
/// <param name="Mask">What the shot collides with, by name.</param>
/// <param name="Blocker">What stops it, by name, if something does.</param>
[Serializable, NetSerializable]
public readonly record struct LineOfFireDebugShot(NetCoordinates From, NetCoordinates To, bool Clear, string Mask, string? Blocker);
