using Content.Shared.DisplacementMap;

namespace Content.Client._KS14.DisplacementMap;

/// <summary>
///     Applies a displacement map to every layer present on this entity's sprite as soon as it is added.
/// </summary>
/// <remarks>
///     Only layers that exist when this component starts up are displaced; layers added afterwards, such as
///     equipped clothing, are not.
/// </remarks>
[RegisterComponent]
[Access(typeof(KsAlwaysDisplacedSystem))]
public sealed partial class KsAlwaysDisplacedComponent : Component
{
    /// <summary>
    ///     The displacement map applied to each of the sprite's layers.
    /// </summary>
    [DataField(required: true)]
    public DisplacementData Displacement = default!;

    /// <summary>
    ///     The layer keys this component has displaced, so the displacement layers can be removed again on
    ///     shutdown regardless of any layer index shifts that happened since.
    /// </summary>
    [ViewVariables]
    public List<string> DisplacedLayerKeys = new();
}
