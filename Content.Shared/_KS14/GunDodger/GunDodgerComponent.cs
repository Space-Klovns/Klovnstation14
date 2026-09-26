using Robust.Shared.GameStates;

namespace Content.Shared._KS14.GunDodger;

/// <summary>
///     7 minutes. 7 minutes is all-
///
///     For dodging bullets shot by non-gundodgers.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(GunDodgerSystem))]
public sealed partial class GunDodgerComponent : Component
{
    /// <summary>
    ///     Speed to throw the dodger at when dodging bullets.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float ThrowSpeed = 10f;

    /// <summary>
    ///     Chance, from 0 to 1, of dodging a given shot. Rolled from a seed the client and server share, so dodges
    ///         are predicted.
    /// </summary>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public float DodgeChance = 1f;
}
