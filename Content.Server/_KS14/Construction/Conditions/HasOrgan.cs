using Content.Shared.Construction;
using Robust.Shared.Prototypes;
using Content.Shared.Body;
using Content.Shared.Examine;

namespace Content.Server._KS14.Construction.Completions;

[DataDefinition]
public sealed partial class HasOrgan : IGraphCondition
{
    [DataField(required: true)]
    public ProtoId<OrganCategoryPrototype> Category = "";

    [DataField]
    public bool Inverted = false;

    public bool Condition(EntityUid uid, IEntityManager entityManager)
    {
        if (!entityManager.TryGetComponent<BodyComponent>(uid, out var bodyComponent))
            return false;

        return bodyComponent.PresentOrganCategories.ContainsKey(Category) ^ Inverted;
    }

    public bool DoExamine(ExaminedEvent args)
    {
        if (Condition(args.Examined, IoCManager.Resolve<IEntityManager>()))
            return false;

        return true;
    }

    private static readonly IEnumerable<ConstructionGuideEntry> EmptyGuideEntry = [];
    public IEnumerable<ConstructionGuideEntry> GenerateGuideEntry() => EmptyGuideEntry;
}
