using Robust.Shared.GameStates;

namespace Content.Shared._Mono.Overlays;

/// <summary>
///     Added to something that is seeing through NV because of some other entity.
///         Only sent to the owner.
/// </summary>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState(raiseAfterAutoHandleState: true), AutoGenerateComponentPause]
public sealed partial class PhosphorNightVisionRecipientComponent : Component
{
    public override bool SendOnlyToOwner => true;

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
}
