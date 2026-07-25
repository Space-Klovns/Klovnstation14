using Robust.Shared.GameStates;

namespace Content.Shared._Mono.Overlays;

[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState, AutoGenerateComponentPause] // KS14
public sealed partial class PhosphorNightVisionRecipientComponent : Component
{
    // KS14 Start
    /// <summary>
    ///     Last ingame time that the user was flashed. If null, means never.
    /// </summary>
    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan? LastFlashTime = null;

    /// <summary>
    ///     Duration of the last time the user was flashed.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan LastFlashDuration;

    [DataField, AutoNetworkedField]
    public EntityUid? NightVisionSourceUid = null;
    // KS14 End
}
