using Content.Shared.Actions;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Perks.Tank;

/// <summary>
///     An action that makes enemy NPCs prefer the holder as a target for <see cref="Duration"/>, by raising their
///         target priority (the server's <c>KsNpcTargetPriorityComponent</c>) to <see cref="Priority"/>.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class KsDrawFireAbilityComponent : Component
{
    [DataField, AutoNetworkedField]
    public TimeSpan Duration = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     The target priority while it lasts, 0 to 1.
    /// </summary>
    [DataField]
    public float Priority = 1f;

    [DataField]
    public SoundSpecifier? Sound = new SoundPathSpecifier("/Audio/Effects/alert.ogg", AudioParams.Default.WithVolume(4f));

    /// <summary>
    ///     When the taunt ends; null while there is none.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField, AutoPausedField]
    public TimeSpan? ActiveUntil;

    /// <summary>
    ///     The priority the holder had before, to put back; null if it had none and the taunt added it.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public float? PreviousPriority;
}

public sealed partial class KsDrawFireActionEvent : InstantActionEvent;
