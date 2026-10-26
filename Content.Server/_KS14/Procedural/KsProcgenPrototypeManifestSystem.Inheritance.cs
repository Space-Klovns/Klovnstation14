using Content.Shared._KS14.Procedural;
using Content.Shared.Maps;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;

namespace Content.Server._KS14.Procedural;

public sealed partial class KsProcgenPrototypeManifestSystem
{
    private bool TryObserveInheritance(Type type, IPrototype? prototype, MappingDataNode declaration,
        ReferenceWork references, out KsProcgenIssue? issue)
    {
        issue = null;
        if (!typeof(IInheritingPrototype).IsAssignableFrom(type)) return true;
        if (!ProtoMan.TryGetKindFrom(type, out var kind))
        { issue = new("UnsupportedPrototypeManifestKind", "Inheritance requires a registered prototype kind."); return false; }

        void Observe(string id)
        {
            if (type == typeof(EntityPrototype)) references.Add(KsProcgenPrototypeBodyKind.Entity, id);
            else if (type == typeof(ContentTileDefinition)) references.Add(KsProcgenPrototypeBodyKind.Tile, id);
            else references.AddDependency(kind, id);
        }

        // Both representations are authoritative inputs. Do not infer links from other strings,
        // or deduplicate occurrences before charging the shared reference allowance.
        foreach (var parent in (prototype as IInheritingPrototype)?.Parents ?? [])
        {
            Observe(parent);
            if (references.Issue != null) { issue = references.Issue; return false; }
        }
        if (!declaration.TryGet("parent", out var node) || node is ValueDataNode { IsNull: true }) return true;
        if (node is ValueDataNode scalar) Observe(scalar.Value);
        else if (node is SequenceDataNode sequence)
        {
            foreach (var entry in sequence)
            {
                if (entry is not ValueDataNode { IsNull: false } value)
                { issue = new("InvalidPrototypeManifestInheritance", "Parent arrays require non-null prototype IDs."); return false; }
                Observe(value.Value);
                if (references.Issue != null) { issue = references.Issue; return false; }
            }
        }
        else
        { issue = new("InvalidPrototypeManifestInheritance", "Parents require a scalar ID or an array of IDs."); return false; }
        issue = references.Issue;
        return issue == null;
    }
}
