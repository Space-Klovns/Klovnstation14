using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Actions;

/// <summary>
///     Makes an action only allowed when (not) in radius of an entity with some component.
///
///     Requires <see cref="ActionComponent"/>.
/// </summary>
[RegisterComponent, NetworkedComponent, Access(typeof(KsRadiusRestrictedActionSystem))]
[EntityCategory("Actions")]
public sealed partial class KsRadiusRestrictedActionComponent : Component
{
    /// <summary>
    ///     Popup shown to client when the action is cancelled.
    /// </summary>
    [DataField(required: true)]
    public LocId Popup = string.Empty;

    /// <summary>
    ///     Components to search for. All of these components
    ///         must be present in an entity to consider it valid.
    /// </summary>
    [DataField(required: true)]
    public ComponentRegistry Components = default!;

    /// <summary>
    ///     Maximum distance from the user to check for entities.
    ///
    ///     Must be a positive, non-zero float.
    /// </summary>
    [DataField(required: true)]
    public float Radius = 0f;

    /// <summary>
    ///     If false, then the action is only allowed in the radius of a valid entity.
    ///         If true, then the action is banned when in the radius of a valid entity.
    /// </summary>
    [DataField]
    public bool Inverted = false;

    /// <summary>
    ///     Should the user be ignored when
    ///         checking for valid entities?
    /// </summary>
    [DataField]
    public bool IgnoreUser = true;
}
