using Robust.Shared.GameStates;

namespace Content.Shared._KS14.SalvageTablet;

/// <summary>
///     On a lathe that has asked the salvagers for materials: when it may ask again. Added by its first request, and
///         networked so the lathe's window can show the wait.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class KsMaterialRequestCooldownComponent : Component
{
    /// <summary>
    ///     How long the lathe waits after a request that went out over the radio.
    /// </summary>
    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(60);

    /// <summary>
    ///     When the lathe may ask again.
    /// </summary>
    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan NextRequestTime;
}
