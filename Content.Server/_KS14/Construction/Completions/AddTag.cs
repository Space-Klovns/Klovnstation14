using Content.Shared.Construction;
using Robust.Shared.Prototypes;
using Content.Shared.Tag;

namespace Content.Server._KS14.Construction.Completions;

[DataDefinition]
public sealed partial class AddTag : IGraphAction
{
    [Dependency] private readonly TagSystem _tagSystem = default!;

    [DataField(required: true)]
    public ProtoId<TagPrototype> Tag = "";

    public void PerformAction(EntityUid uid, EntityUid? userUid, IEntityManager entityManager)
    {
        _tagSystem.AddTag(uid, Tag);
    }
}
