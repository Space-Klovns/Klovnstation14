using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._KS14.GunDodger;

/// <summary>
///     7 minutes. 7 minutes is all-
///
///     For dodging bullets shot by non-gundodgers.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
[Access(typeof(GunDodgerSystem))]
public sealed partial class GunDodgerComponent : Component
{
    /// <summary>
    ///     Speed to throw the dodger at when dodging bullets.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float ThrowSpeed = 10f;

    /// <summary>
    ///     Chance, from 0 to 1, of dodging a given shot. Rolled once per shot, from the dodger and the tick, so the
    ///         client and server agree on it. At 1, every projectile passes through, dodge window or not.
    /// </summary>
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public float DodgeChance = 1f;

    /// <summary>
    ///     How long after a successful dodge projectiles pass through the dodger.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan DodgeWindow = TimeSpan.FromSeconds(0.7);

    /// <summary>
    ///     Until when projectiles pass through, following the last successful dodge.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan DodgeEndTime;
}
