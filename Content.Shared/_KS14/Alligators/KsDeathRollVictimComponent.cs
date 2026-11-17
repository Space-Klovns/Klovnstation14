using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Alligators;

/// <summary>
///     Marks someone held in an alligator's death roll, so clients can lay them out along the alligator
///     and roll them with it, whichever way they are trying to face, drawn under the alligator.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class KsDeathRollVictimComponent : Component
{
    /// <summary>
    ///     The alligator holding this entity.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? Alligator;

    /// <summary>
    ///     Game time at which the roll started.
    /// </summary>
    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan StartTime;

    /// <summary>
    ///     How long the victim shows each facing while being rolled over.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan StepTime = TimeSpan.FromSeconds(0.2);

    /// <summary>
    ///     The victim's draw depth before the client put them under the alligator. Client only.
    /// </summary>
    [ViewVariables]
    public int? OriginalDrawDepth;
}
