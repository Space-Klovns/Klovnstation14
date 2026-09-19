using Content.Shared.Body;
using Robust.Shared.Prototypes;

namespace Content.Client._KS14.Klovnmed.LimbDamageVisuals;

[RegisterComponent]
public sealed partial class LimbDamageVisualsComponent : Component
{
    [DataField]
    public Dictionary<Enum, ProtoId<OrganCategoryPrototype>> RequiredOrgans = [];
}
