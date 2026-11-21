using Content.Shared._KS14.Cultivation.Components;
using Content.Shared.Construction;
using Content.Shared.Construction.Conditions;
using JetBrains.Annotations;
using Robust.Shared.Map;

namespace Content.Shared._KS14.Cultivation;

/// <summary>
///     Only buildable in a wuxia round: some station has to be marked with <see cref="WuxiaRoundComponent"/>.
/// </summary>
[UsedImplicitly]
[DataDefinition]
public sealed partial class WuxiaRoundCondition : IConstructionCondition
{
    public bool Condition(EntityUid user, EntityCoordinates location, Direction direction)
    {
        var entManager = IoCManager.Resolve<IEntityManager>();
        var query = entManager.EntityQueryEnumerator<WuxiaRoundComponent>();
        return query.MoveNext(out _, out _);
    }

    public ConstructionGuideEntry GenerateGuideEntry()
    {
        return new ConstructionGuideEntry
        {
            Localization = "construction-step-condition-wuxia-round",
        };
    }
}
