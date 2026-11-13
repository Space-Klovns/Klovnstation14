using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.FieldCommander;

/// <summary>
///     Marks a body as one of the field commander's defenders, for the client, which cannot see the rule. The server
///         puts it on every defender's body, with the look of their going down copied from the rule.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class KsFieldCommanderDefenderComponent : Component
{
    /// <summary>
    ///     The flash over the defender as they go critical.
    /// </summary>
    [DataField, AutoNetworkedField]
    public KsFieldCommanderDownFlashSettings CriticalFlash = new();

    /// <summary>
    ///     The flash over the defender as they die.
    /// </summary>
    [DataField, AutoNetworkedField]
    public KsFieldCommanderDownFlashSettings DeadFlash = new();
}

/// <summary>
///     The look of one flash of static over a defender going down.
/// </summary>
[DataDefinition, Serializable, NetSerializable]
public sealed partial class KsFieldCommanderDownFlashSettings
{
    /// <summary>
    ///     The colour of the static. Its alpha is how strong it is as it starts, before it fades.
    /// </summary>
    [DataField]
    public Color Color = Color.FromHex("#FF2020C0");

    /// <summary>
    ///     How long the flash lasts, in seconds.
    /// </summary>
    [DataField]
    public float Duration = 0.45f;
}
