using Robust.Shared.GameStates;

namespace Content.Shared._KS14.WallHealer;

/// <summary>
///     How much treatment an ID card's holder can still get from wall healers. Added to an ID card the first time a
///         healer charges it. What has been spent recovers steadily, so the allowance is worked out when it is read
///         from <see cref="Spent"/> and <see cref="SpentAt"/>, rather than ticked up.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
[Access(typeof(KsWallHealerSystem))]
public sealed partial class KsWallHealerAllowanceComponent : Component
{
    /// <summary>
    ///     The most treatment the card allows when nothing has been spent, in the units of a healer's pools.
    /// </summary>
    [DataField]
    public float Limit = 100f;

    /// <summary>
    ///     How long a fully spent allowance takes to recover.
    /// </summary>
    [DataField]
    public TimeSpan RecoveryTime = TimeSpan.FromMinutes(10);

    /// <summary>
    ///     How much had been spent at <see cref="SpentAt"/>.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Spent;

    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan SpentAt;
}
