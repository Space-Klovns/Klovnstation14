using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Carrying;

/// <summary>
///     Someone carrying <see cref="Carried"/> on their back.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(KsFiremanCarrySystem))]
public sealed partial class KsCarryingComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid Carried;
}
