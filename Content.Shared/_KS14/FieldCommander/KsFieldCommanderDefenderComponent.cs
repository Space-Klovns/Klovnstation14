using Content.Shared.Mobs;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.FieldCommander;

/// <summary>
///     Marks a body as one of the field commander's defenders, for the client, which cannot see the rule. The server
///         puts it on every defender's body, with the look of their going down copied from the rule.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(KsFieldCommanderDefenderSystem), Other = AccessPermissions.ReadExecute)]
public sealed partial class KsFieldCommanderDefenderComponent : Component
{
    /// <summary>
    ///     The flash over the defender as they go into each state that has one. Read-only: the server copies the
    ///         dictionary from the rule (<see cref="KsFieldCommanderDefenderSystem.SetFlashes"/>), but the settings in it are shared.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Dictionary<MobState, KsFieldCommanderDownFlashSettings> Flashes = new()
    {
        [MobState.Critical] = new(),
        [MobState.Dead] = new(),
    };
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
    ///     How long the flash lasts.
    /// </summary>
    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(2);
}
