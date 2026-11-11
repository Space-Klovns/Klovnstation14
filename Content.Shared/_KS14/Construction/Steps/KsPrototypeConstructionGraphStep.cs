using Content.Shared.Construction.Steps;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Construction.Steps;

/// <summary>
///     Inserts an entity spawned from <see cref="Prototype"/> or from a prototype inheriting from it, like
///         tgstation's recipe <c>reqs</c>. For items that can't be given a tag of their own from a fork file
///         without hiding the tags they inherit.
/// </summary>
[DataDefinition]
public sealed partial class KsPrototypeConstructionGraphStep : ArbitraryInsertConstructionGraphStep
{
    [DataField(required: true)]
    public EntProtoId Prototype;

    public override bool EntityValid(EntityUid uid, IEntityManager entityManager, IComponentFactory compFactory)
    {
        return IoCManager.Resolve<IPrototypeManager>().IsOrInheritsFrom(entityManager, uid, Prototype);
    }
}
