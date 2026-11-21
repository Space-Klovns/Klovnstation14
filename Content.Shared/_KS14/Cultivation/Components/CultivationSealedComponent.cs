using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._KS14.Cultivation.Components;

/// <summary>
///     Cultivation locked away by the eight trigrams: no techniques, no body arts, no martial arts until it fades.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class CultivationSealedComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan Until;
}
