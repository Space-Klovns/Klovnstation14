using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Impacts;

/// <summary>
///     What a bullet hitting this throws off and sounds like. Without one, a hit on something that is not a mob only
///         puffs <see cref="KsImpactSurfaceSystem.FallbackMaterial"/>'s smoke, as TGMC does for every object.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class KsImpactSurfaceComponent : Component
{
    [DataField(required: true)]
    public ProtoId<KsImpactMaterialPrototype> Material;
}
