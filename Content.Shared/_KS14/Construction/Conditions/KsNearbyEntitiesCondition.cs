using Content.Shared.Construction;
using Content.Shared.Construction.Conditions;
using Content.Shared.Tools;
using Content.Shared.Whitelist;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Construction.Conditions;

/// <summary>
///     Requires items to be held, worn or near the user without using them up. tgstation's <c>tool_paths</c> and
///         <c>tool_behaviors</c> on crafting recipes. Not shown in the construction menu: working out what a secret
///         recipe needs is part of it.
/// </summary>
[DataDefinition]
public sealed partial class KsNearbyEntitiesCondition : IConstructionCondition
{
    [DataField(required: true)]
    public List<KsNearbyEntityEntry> Entries = new();

    public bool Condition(EntityUid user, EntityCoordinates location, Direction direction)
    {
        return IoCManager.Resolve<IEntityManager>().System<SharedKsNearbyEntitiesSystem>().AllPresent(user, Entries);
    }

    public ConstructionGuideEntry? GenerateGuideEntry()
    {
        return null;
    }
}

[DataDefinition]
public sealed partial class KsNearbyEntityEntry
{
    [DataField]
    public EntityWhitelist? Whitelist;

    /// <summary>
    ///     Entities spawned from one of these prototypes, or from a prototype inheriting from one, count.
    /// </summary>
    [DataField]
    public List<EntProtoId>? Prototypes;

    [DataField]
    public ProtoId<ToolQualityPrototype>? Tool;
}
