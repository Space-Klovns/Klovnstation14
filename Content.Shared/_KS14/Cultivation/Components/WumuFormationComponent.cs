using Content.Shared._KS14.Cultivation.Systems;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._KS14.Cultivation.Components;

/// <summary>
///     Rallied by the Wumu Yishu: faster on their feet and half as easily worn down, until the formation breaks up.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause, Access(typeof(WumuFormationSystem))]
public sealed partial class WumuFormationComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan Until;

    [DataField, AutoNetworkedField]
    public float SpeedMultiplier = 1.25f;

    [DataField, AutoNetworkedField]
    public float StaminaMultiplier = 0.5f;
}
