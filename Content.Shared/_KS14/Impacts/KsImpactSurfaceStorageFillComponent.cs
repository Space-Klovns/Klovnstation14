using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Impacts;

/// <summary>
///     A hit uses this <see cref="Material"/> instead of the <see cref="KsImpactSurfaceComponent"/>'s with a chance of
///         how full the entity's storage is: a full bookshelf throws mostly paper, an empty one only wood.
/// </summary>
[RegisterComponent, NetworkedComponent, Access(typeof(KsImpactSurfaceSystem))]
public sealed partial class KsImpactSurfaceStorageFillComponent : Component
{
    [DataField(required: true)]
    public ProtoId<KsImpactMaterialPrototype> Material;

    /// <summary>
    ///     How full the storage is, 0 to 1. Worked out by <see cref="KsImpactSurfaceSystem"/> whenever its contents
    ///         change, on each side from the contents it has, so a hit only reads it.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public float FillFraction;
}
