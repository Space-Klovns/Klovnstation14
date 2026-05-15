using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._KS14.ScanDiscoverable.Feedback;

/// <summary>
///     Audio feedback for discovering something.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class KsDiscoveringScannerFeedbackComponent : Component
{
    /// <summary>
    ///     Sound played on the scanner after discovering something.
    /// </summary>
    [DataField(required: true)]
    public SoundSpecifier UseSoundSpecifier;
}
