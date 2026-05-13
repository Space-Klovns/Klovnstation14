using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Clothing.Components;

/// <summary>
/// Marker component granted to an entity wearing clothes that require event relaying.
/// When this component exists on an entity, events like MobStateChanged will be relayed to worn clothing with WornRelayEventComponent.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ClothingRelayEventRequiredComponent : Component
{
}
