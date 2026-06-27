using Content.Shared.DoAfter;
using Content.Shared.Trigger.Components.Effects;
using Content.Shared.Trigger.Systems;
using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Trigger.Components;

[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState(fieldDeltas: true), AutoGenerateComponentPause]
public sealed partial class DoAfterOnTriggerComponent : BaseXOnTriggerComponent
{
    [DataField, AutoNetworkedField]
    public TimeSpan Duration;

    [DataField, AutoNetworkedField]
    public string? StartKeyOut = null;

    [DataField, AutoNetworkedField]
    public string? CancelledKeyOut = null;

    [DataField, AutoNetworkedField]
    public string? KeyOut = TriggerSystem.DefaultTriggerKey;

    // Cooldown

    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan NextAllowedTime = TimeSpan.MinValue;

    /// <summary>
    ///     Cooldown between attempts to start the doafter.
    ///         If zero, then the cooldown does not apply.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan Cooldown = TimeSpan.Zero;

    /// <summary>
    ///     If not null, then this popup will be shown to the user
    ///         when they try to do a do-after, but the cooldown has not passed.
    ///
    ///     This gets a param called `time` which is the number of seconds (with 1 digit of decimal at most)
    ///          left until the cooldown expires.
    /// </summary>
    [DataField, AutoNetworkedField]
    public LocId? CooldownPopupLoc = null;

    // Max users

    /// <summary>
    ///     If true, no new do-afters can be started when one is ongoing.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool BlockDuplicate = false;

    /// <summary>
    ///     If not null, then this popup will be shown to the user
    ///         when they try to do a do-after, but their is already someone
    ///         using this and <see cref="BlockDuplicate"/> is true.
    /// </summary>
    [DataField, AutoNetworkedField]
    public LocId? DuplicatePopupLoc = null;

    [DataField, AutoNetworkedField]
    public EntityUid? CurrentUserUid = null;
}
