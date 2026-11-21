using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Cultivation.Body;

/// <summary>
///     Heat Sight: the warmth of living things shows through walls, as glowing silhouettes, within a range.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class KsHeatSightComponent : Component
{
    [DataField, AutoNetworkedField]
    public float Range = 8f;
}
