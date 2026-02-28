using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Clothing.Components;

/// <summary>
/// KS14 - when worn this relays mobstatechange events to the entity that grants it.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WornHealthMonitorComponent : Component
{
}
