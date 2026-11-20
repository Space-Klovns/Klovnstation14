using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Carrying;

/// <summary>
///     Someone carried on <see cref="Carrier"/>'s back. Parented to the carrier, without collision, until put down.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(KsFiremanCarrySystem))]
public sealed partial class KsBeingCarriedComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid Carrier;
}
