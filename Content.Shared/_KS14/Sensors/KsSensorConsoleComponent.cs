using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Sensors;

/// <summary>
///     Marks a shuttle/radar console as a fork sensor console: one whose nav state should
///         carry the grid's sensor contact picture (fog of war, coverage fans, emitter
///         state). A vanilla console has no such marker, so the sensor framework never
///         touches its state and it behaves exactly like upstream. See
///         KsSensorConsoleSystem.
/// </summary>
[RegisterComponent]
public sealed partial class KsSensorConsoleComponent : Component;

/// <summary>
///     Toggles the console grid's radar emission (on == emitting). Flips every radar on
///         the grid together: running active radar lights you up for enemy ELINT, so going
///         silent is a deliberate, reversible choice made at the console.
/// </summary>
[Serializable, NetSerializable]
public sealed class KsToggleRadarMessage : BoundUserInterfaceMessage;

/// <summary>
///     Toggles the console grid's jamming, flipping every jammer on the grid together like
///         the radar toggle. An active jammer floods a wedge with noise (blinding radars in
///         it) but also broadcasts itself to enemy ELINT and burns power.
/// </summary>
[Serializable, NetSerializable]
public sealed class KsToggleJammerMessage : BoundUserInterfaceMessage;

/// <summary>
///     Starts ELINT focus analysis on one designated emitter contact, pointing every ELINT
///         array on the grid at it (one focus per grid, like the grid-wide emitter
///         toggles). The server only accepts a target the grid's own pool has actually
///         filed as an emitter, so the message can never be used to probe unheard grids.
/// </summary>
[Serializable, NetSerializable]
public sealed class KsElintFocusMessage : BoundUserInterfaceMessage
{
    public NetEntity Target;
}

/// <summary>
///     Stops the grid's ELINT focus analysis. Clearing (or retargeting) resets analysis
///         progress; intel already unlocked stays known (it is sticky on the contact).
/// </summary>
[Serializable, NetSerializable]
public sealed class KsElintClearFocusMessage : BoundUserInterfaceMessage;
