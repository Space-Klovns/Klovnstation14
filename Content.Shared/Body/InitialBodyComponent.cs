using Robust.Shared.GameStates; // KS14
using Robust.Shared.Prototypes;

namespace Content.Shared.Body;

/// <summary>
/// On map initialization, spawns the given organs into the body.
/// Liable to change as the body becomes more complex.
/// </summary>
[RegisterComponent]
[NetworkedComponent, AutoGenerateComponentState] // KS14
[Access(typeof(InitialBodySystem))]
public sealed partial class InitialBodyComponent : Component
{
    /// <summary>
    /// The organs to spawn based on their category.
    /// </summary>
    [DataField(required: true)]
    public List<InitialBodyPart> Organs; // KS14: use initialbodypart

    // KS14
    [AutoNetworkedField]
    public HashSet<ProtoId<OrganCategoryPrototype>> TotalCategories = [];
}

// KS14
[DataDefinition]
public sealed partial class InitialBodyPart
{
    [DataField(required: true)]
    public ProtoId<OrganCategoryPrototype> Category = "";

    [DataField(required: true)]
    public EntProtoId<OrganComponent> Entity = "";

    [DataField]
    public List<InitialBodyPart>? Children = null;
}
