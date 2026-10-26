using System.Text;
using System.Linq;
using Content.Shared._KS14.Procedural;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Validation;
using Robust.Shared.Serialization.Markdown.Value;

namespace Content.Server._KS14.Procedural;

public sealed partial class KsProcgenPrototypeManifestSystem
{
    private bool TryCaptureDependency(KsProcgenPrototypeReference reference, PrototypeContext context,
        ReferenceWork references, CaptureWork work, out KsProcgenPrototypeDependencyBody? body, out KsProcgenIssue? issue)
    {
        body = null; issue = null;
        if (!ProtoMan.TryGetKindType(reference.Kind, out var type))
        { issue = new("UnsupportedPrototypeManifestKind", "A dependency kind is no longer registered."); return false; }
        if (!ProtoMan.TryGetMapping(type, reference.PrototypeId, out var declaration))
        { issue = new("MissingPrototypeManifestDependency", "A typed dependency lacks its resolved declaration."); return false; }
        var declared = new StringBuilder();
        if (!work.Write(declaration, declared, 0))
        { issue = new(work.IssueCode!, "Dependency declarations share the manifest structural allowance."); return false; }
        var declarationOnly = !ProtoMan.TryIndex(type, reference.PrototypeId, out var prototype);
        if (declarationOnly)
        {
            if (!declaration.TryGet<ValueDataNode>("abstract", out var flag) ||
                !bool.TryParse(flag.Value, out var abstractPrototype) || !abstractPrototype)
            { issue = new("MissingPrototypeManifestDependency", "A typed dependency has no loaded definition or abstract declaration."); return false; }
            // Inspect declared typed fields without constructing partial prototypes or running hooks.
            // It remains declaration-only; this never registers or grants spawn authority to it.
            if (!TryObserveAbstract(type, reference.PrototypeId, declaration, context, references, work, out var runtimeType, out issue)) return false;
            body = new(reference.Kind, reference.PrototypeId, runtimeType!, true, string.Empty, declared.ToString());
            return true;
        }
        if (prototype == null || prototype.ID != reference.PrototypeId)
        { issue = new("UnsupportedPrototypeManifestDependency", "A typed dependency must retain its registered identity."); return false; }
        if (!TryObserveInheritance(type, prototype, declaration, references, out issue)) return false;
        var node = _serializationManager.WriteValue(prototype.GetType(), prototype, alwaysWrite: true, context: context, notNullableOverride: true);
        if (references.Issue != null) { issue = references.Issue; return false; }
        if (node is not MappingDataNode)
        { issue = new("UnsupportedPrototypeManifestDependency", "Typed dependencies require explicit definition mappings."); return false; }
        var canonical = new StringBuilder();
        if (!work.Write(node, canonical, 0))
        { issue = new(work.IssueCode!, "Dependency bodies and declarations share the manifest structural allowance."); return false; }
        body = new(reference.Kind, reference.PrototypeId, prototype.GetType().FullName ?? type.Name,
            declarationOnly, declarationOnly ? string.Empty : canonical.ToString(), declared.ToString());
        return true;
    }

    private bool TryObserveAbstract(Type type, string id, MappingDataNode declaration, PrototypeContext context,
        ReferenceWork references, CaptureWork work, out string? runtimeType, out KsProcgenIssue? issue)
    {
        runtimeType = null; issue = null;
        if (!TryObserveInheritance(type, null, declaration, references, out issue)) return false;
        // Abstract bases can deliberately omit required component fields. Inspect only their
        // declared schema; constructing a fake complete prototype would invent defaults or fail.
        var fields = new MappingDataNode { Tag = declaration.Tag };
        foreach (var (key, value) in declaration.Children)
        {
            if (key is "type" or "parent") continue;
            fields.Add(key, value);
            if (declaration.GetKeyTag(key) is { } tag) fields.SetKeyTag(key, tag);
        }
        var validation = _serializationManager.ValidateNode(type, fields, context: context);
        if (references.Issue != null) { issue = references.Issue; return false; }
        if (validation.GetErrors().Any(error => error is PrototypeWorldReferenceErrorNode))
        { issue = new("UnsupportedPrototypeManifestBody", "Abstract declarations cannot retain live entity, network or map references."); return false; }
        var inspection = new StringBuilder();
        if (!validation.Valid)
        {
            // Partial/legacy declarations can contain fields absent from the registered server
            // schema. Retain explicit stable gaps alongside the original input, never guess IDs
            // inside opaque fields or pretend validation implies complete reference discovery.
            var gaps = new SequenceDataNode();
            foreach (var gap in validation.GetErrors().Select(error => error is FieldNotFoundErrorNode missing
                         ? "UnrecognizedField|" + missing.FieldType.FullName + "|" + ((ValueDataNode) missing.Node).Value
                         : "UnsupportedDeclaredSchema|" + error.GetType().FullName + "|" + error.Node.GetType().FullName)
                         .Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal))
                gaps.Add(new ValueDataNode(gap));
            if (!work.Write(gaps, inspection, 0))
            { issue = new(work.IssueCode!, "Abstract inspection diagnostics share the manifest structural allowance."); return false; }
        }
        var inspected = new StringBuilder();
        if (!work.Write(fields, inspected, 0, normalizeEntity: type == typeof(EntityPrototype)))
        { issue = new(work.IssueCode!, "Abstract reference discovery shares the manifest structural allowance."); return false; }
        runtimeType = type.FullName ?? type.Name;
        if (!ProtoMan.TryGetKindFrom(type, out var kind))
        { issue = new("UnsupportedPrototypeManifestKind", "An abstract template requires a registered kind."); return false; }
        references.AbstractTemplates.Add(new(kind, id, runtimeType, inspected.ToString(), inspection.ToString()));
        return true;
    }
}
