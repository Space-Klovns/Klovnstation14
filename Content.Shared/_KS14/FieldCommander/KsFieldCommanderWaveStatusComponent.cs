using Robust.Shared.GameStates;

namespace Content.Shared._KS14.FieldCommander;

/// <summary>
///     On a map: how far the field commander's waves have got, for the defenders' HUD.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class KsFieldCommanderWaveStatusComponent : Component
{
    /// <summary>
    ///     How many waves have been sent.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int Wave;

    /// <summary>
    ///     When the next wave is due, or null if none is scheduled.
    /// </summary>
    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan? NextWaveTime;
}
