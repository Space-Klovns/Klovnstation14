using Content.Shared.Construction;
using Content.Shared.Examine;
using Content.Shared.Tag;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Construction.Conditions;

[DataDefinition]
public sealed partial class HasTag : IGraphCondition
{
    [Dependency] private readonly TagSystem _tagSystem = default!;

    [DataField(required: true)]
    public ProtoId<TagPrototype> Tag = "";

    [DataField]
    public bool Inverted = false;

    public bool Condition(EntityUid uid, IEntityManager entityManager) => _tagSystem.HasTag(uid, Tag) ^ Inverted;

    public bool DoExamine(ExaminedEvent args)
    {
        if (Condition(args.Examined, IoCManager.Resolve<IEntityManager>()))
            return false;

        return true;
    }

    private static readonly IEnumerable<ConstructionGuideEntry> EmptyGuideEntry = [];
    public IEnumerable<ConstructionGuideEntry> GenerateGuideEntry() => EmptyGuideEntry;
}
