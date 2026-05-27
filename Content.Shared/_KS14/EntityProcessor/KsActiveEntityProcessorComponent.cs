using Robust.Shared.GameStates;

namespace Content.Shared._KS14.EntityProcessor;

/// <summary>
///     Added to object processors that are actively processing something.
/// </summary>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState, AutoGenerateComponentPause]
[Access(typeof(KsEntityProcessorSystem))]
public sealed partial class KsActiveEntityProcessorComponent : Component
{
    /// <summary>
    ///     Dictionary of entities being processed, and timespan of the delay until theyre finished.
    /// </summary>
    [DataField]
    [AutoNetworkedField]
    [AutoPausedField] // Yes, really, this works on dicts
    public Dictionary<EntityUid, TimeSpan> Processing = [];
}
