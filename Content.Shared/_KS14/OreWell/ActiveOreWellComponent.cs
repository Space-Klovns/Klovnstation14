using Content.Shared.Materials;
using Content.Shared.Stacks;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.OreWell;

/// <summary>
///     Component added to ore vents that have been
///         tapped and that have had ore vents installed on them.
/// </summary>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
[Access(typeof(OreWellSystem), Other = AccessPermissions.Read)]
public sealed partial class ActiveOreWellComponent : Component
{
    /// <summary>
    ///     ID for the prototype that contains the settings for this.
    /// </summary>
    [DataField(required: true, serverOnly: true)]
    [ViewVariables(VVAccess.ReadWrite)]
    public ProtoId<OreWellSettingPrototype> SettingId;

    /// <summary>
    ///     Rate of ore generated, per ore.
    ///         In ore/second.
    /// </summary>
    [DataField, AutoNetworkedField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float IndividualResourceRate = 0f;

    /// <summary>
    ///     Each ore that is generated.
    /// </summary>
    [DataField, AutoNetworkedField]
    [ViewVariables(VVAccess.ReadWrite)]
    public ProtoId<StackPrototype>[] ResourceTypes = [];
}
