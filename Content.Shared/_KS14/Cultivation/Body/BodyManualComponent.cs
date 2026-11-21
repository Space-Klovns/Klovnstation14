using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Cultivation.Body;

/// <summary>
///     A manual of the Body Molding Art. The primer starts anyone on Copper Skin; the full art commits the reader
///         to the path of the flesh, which closes the way of qi for good. Qi cultivators can read neither.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class BodyManualComponent : Component
{
    [DataField]
    public bool Commits;

    [DataField]
    public TimeSpan ReadTime = TimeSpan.FromSeconds(4);
}
