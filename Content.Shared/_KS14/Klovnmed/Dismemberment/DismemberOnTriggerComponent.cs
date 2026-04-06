using Content.Shared.Trigger.Components.Effects;
using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Klovnmed.Dismemberment;

/// <summary>
///     Dismembers a bodypart on trigger.
/// </summary>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
public sealed partial class DismemberOnTriggerComponent : BaseXOnTriggerComponent
{
    /// <summary>
    ///     Range of minimum and maximum number of sparks to emit upon trigger.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public BodyPartType PartType = BodyPartType.Leg;

    /// <summary>
    ///     Throw-speed of bodypart being dismembered.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float ThrowSpeed = 10f;
}
