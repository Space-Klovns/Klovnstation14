using Content.Shared.Construction;
using Robust.Shared.Prototypes;
using Content.Shared.Body;
using Content.Server._KS14.Klovnmed.OrganAttachmentConstruction;

namespace Content.Server._KS14.Construction.Completions;

[DataDefinition]
public sealed partial class AddAttachableOrganCategory : IGraphAction
{
    [Dependency] private readonly OrganAttachmentConstructionSystem _organAttachmentConstructionSystem = default!;

    [DataField(required: true)]
    public ProtoId<OrganCategoryPrototype> Category = "";

    public void PerformAction(EntityUid uid, EntityUid? userUid, IEntityManager entityManager)
    {
        _organAttachmentConstructionSystem.AddAttachableCategory(uid, Category);
    }
}
