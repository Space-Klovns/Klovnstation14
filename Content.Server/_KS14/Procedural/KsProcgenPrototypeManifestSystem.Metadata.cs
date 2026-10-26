using System.Linq;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;
using Content.Shared._KS14.Procedural;

namespace Content.Server._KS14.Procedural;

public sealed partial class KsProcgenPrototypeManifestSystem
{
    private bool TryMetadataNode(EntityPrototype prototype, KsProcgenPrototypeManifestLimits limits,
        PrototypeContext context, out DataNode? node, out KsProcgenIssue? issue)
    {
        node = null; issue = null;
        if (prototype.Categories.Count > limits.MaximumCategoriesPerEntity)
        { issue = new("PrototypeManifestCategoryBudget", "Resolved category metadata exceeds the per-entity allowance."); return false; }
        var categories = new SequenceDataNode();
        var network = new MappingDataNode();
        foreach (var (name, entry) in prototype.Components.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            network.Add(name, new ValueDataNode(entry.Component.NetSyncEnabled ? "true" : "false"));
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var category in prototype.Categories.OrderBy(category => category.ID, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(category.ID) || category.ID.Length > 256 || !ids.Add(category.ID) ||
                category.Components?.Count > limits.MaximumComponentsPerEntity)
            { issue = new("UnsupportedPrototypeManifestMetadata", "Resolved categories require bounded unique IDs and component sets."); return false; }
            categories.Add(_serializationManager.WriteValue(category, alwaysWrite: true, context: context, notNullableOverride: true));
        }
        node = new MappingDataNode
        {
            { "hideSpawnMenu", new ValueDataNode(prototype.HideSpawnMenu ? "true" : "false") },
            { "categories", categories },
            { "componentNetSync", network },
        };
        return true;
    }
}
