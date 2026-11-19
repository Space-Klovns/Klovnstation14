using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Vehicle;

/// <summary>
///     On a projectile fired by a vehicle's operator: it passes through that vehicle, which its shooter sits on or in.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(KsRiderProjectileSystem))]
public sealed partial class KsRiderProjectileComponent : Component
{
    /// <summary>
    ///     The vehicle the shooter was operating when they fired.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public EntityUid? Vehicle;
}
