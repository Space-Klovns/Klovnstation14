using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Actions;

/// <summary>
///     An action that always returns to the action bar, even if a saved bar layout once dropped it. For actions a
///         player cannot do without, like Forge the Body.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class AlwaysOnBarComponent : Component;
