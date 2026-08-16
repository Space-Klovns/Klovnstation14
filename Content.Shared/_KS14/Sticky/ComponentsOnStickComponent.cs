using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Sticky;

/// <summary>
///     Adds components to itself when sticking to something, and removes them when unsticking.
/// </summary>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
public sealed partial class ComponentsOnStickComponent : Component
{
    /// <summary>
    ///     If the thing being stuck to must be an occluder
    ///         for these comps to be added.
    /// </summary>
    [DataField, ViewVariables]
    public bool RequiresOccluder = true;

    [DataField(required: true), ViewVariables]
    public ComponentRegistry Components;

    /// <summary>
    ///     Whether the components are already added
    /// </summary>
    [DataField, ViewVariables]
    [AutoNetworkedField]
    public bool ComponentsGotAdded = false;
}
