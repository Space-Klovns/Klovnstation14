using Robust.Shared.GameStates;

namespace Content.Shared._KS14.OverlayStains;

/// <summary>
///     Component to visualise blood-stains on things.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class StainedComponent : Component
{
    /// <summary>
    ///     Stains that are on this entity.
    /// </summary>
    [AutoNetworkedField]
    public List<StainData> Stains = new();

    /// <summary>
    ///     Was a <see cref="Chemistry.Reaction.ReactiveComponent"/> created
    ///         on this entity after being stained?
    /// </summary>
    [AutoNetworkedField]
    public bool OwnsBoundReactiveComponent = false;
}
