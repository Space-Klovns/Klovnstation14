using Robust.Shared.GameStates;
using Robust.Shared.Map;

namespace Content.Shared._KS14.DodgingEffect;

[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState(fieldDeltas: true), AutoGenerateComponentPause]
[Access(typeof(DodgingEffectSystem))]
public sealed partial class DodgingEffectComponent : Component
{
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    [AutoNetworkedField]
    public TimeSpan Interval = TimeSpan.FromSeconds(0.1d);

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    [AutoNetworkedField, AutoPausedField]
    public TimeSpan StartTime = TimeSpan.MinValue;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    [AutoNetworkedField, AutoPausedField]
    public TimeSpan TimeUntilNextEffect = TimeSpan.MinValue;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    [AutoNetworkedField, AutoPausedField]
    public TimeSpan TimeUntilFinished = TimeSpan.MaxValue;

    /// <summary>
    ///     A set of truncated DodgingEffectDatums
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public List<EntityCoordinates> Data = [];
}
