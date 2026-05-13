using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Clothing.Components;

/// <summary>
/// KS14 - used to relay events to clothing similar to how implanters do it.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WornRelayEventComponent : Component
{
}
