using Content.Shared.Polymorph;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Polymorph.Components;

[RegisterComponent]
[NetworkedComponent]
[AutoGenerateComponentState]
public sealed partial class PolymorphableComponent : Component
{
    /// <summary>
    /// A list of all the polymorphs that the entity has.
    /// Used to manage them and remove them if needed.
    /// </summary>
    [DataField]
    [AutoNetworkedField]
    public Dictionary<ProtoId<PolymorphPrototype>, EntityUid> PolymorphActions = new();

    /// <summary>
    /// Timestamp for when the most recent polymorph ended.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    [DataField]
    [AutoNetworkedField]
    public TimeSpan? LastPolymorphEnd = default!;

    /// <summary>
    /// The polymorphs that the entity starts out being able to do.
    /// </summary>
    [DataField]
    [AutoNetworkedField]
    public List<ProtoId<PolymorphPrototype>>? InnatePolymorphs;

    /// <summary>
    /// [KS14]
    /// The current existing PolymorphedEntity which is the child of this Entity
    /// </summary>
    [DataField]
    [AutoNetworkedField]
    public NetEntity? ChildEntity = default!;
}
