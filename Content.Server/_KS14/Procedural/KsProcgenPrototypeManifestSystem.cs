using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Content.Shared._KS14.Procedural;
using Content.Shared.Maps;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;

namespace Content.Server._KS14.Procedural;

public enum KsProcgenPrototypeBodyKind : byte { Entity, Tile, EntityDeclaration, TileDeclaration }
public sealed record KsProcgenPrototypeRoot(KsProcgenPrototypeBodyKind Kind, string PrototypeId);
public sealed record KsProcgenPrototypeBody(KsProcgenPrototypeBodyKind Kind, string PrototypeId,
    ushort RuntimeTileId, string CanonicalBody, string CanonicalDeclaration, string CanonicalMetadata);
public sealed record KsProcgenPrototypeReference(string Kind, string PrototypeId);
public sealed record KsProcgenPrototypeDependencyBody(string Kind, string PrototypeId, string RuntimeType,
    bool DeclarationOnly, string CanonicalBody, string CanonicalDeclaration);
public sealed record KsProcgenPrototypeAbstractTemplate(string Kind, string PrototypeId, string RuntimeType,
    string CanonicalFields, string CanonicalInspection)
{
    /// <summary>Registered validators could not inspect some declared fields. This is not complete reference coverage.</summary>
    public bool HasDeclaredInspectionGaps => CanonicalInspection.Length != 0;
}

public sealed record KsProcgenPrototypeManifestLimits
{
    // Schema ten includes typed kinds and inheritance declarations. The measured native office
    // has 649 combined definitions; retain bounded headroom without omitting ancestors.
    public int MaximumDefinitions { get; init; } = 1024;
    public int MaximumReferences { get; init; } = 32_768;
    public int MaximumComponentsPerEntity { get; init; } = 256;
    public int MaximumCategoriesPerEntity { get; init; } = 64;
    public int MaximumNodes { get; init; } = 262_144;
    public int MaximumCharacters { get; init; } = 4_194_304;
    public int MaximumDepth { get; init; } = 64;
    public KsProcgenResourceCaptureLimits Resources { get; init; } = new();
}

/// <summary>
/// Detached candidate bodies and registered typed prototype-reference closure. Legacy reference
/// strings and ignored client-only schemas still need dependency discovery. Observed resource paths
/// retain bounded detached bytes; this is not proof of all resource or native appearance coverage.
/// </summary>
public sealed class KsProcgenPrototypeManifest
{
    internal KsProcgenPrototypeManifest(IEnumerable<KsProcgenPrototypeBody> bodies,
        KsProcgenPrototypeManifestLimits limits, int nodes, int characters, int referenceOccurrences, IEnumerable<KsProcgenPrototypeRoot>? roots = null,
        IEnumerable<string>? entityReferenceSeeds = null,
        IEnumerable<KsProcgenPrototypeReference>? dependencyRoots = null,
        IEnumerable<KsProcgenPrototypeDependencyBody>? dependencies = null,
        IEnumerable<KsProcgenPrototypeAbstractTemplate>? abstractTemplates = null,
        IEnumerable<KsProcgenPrototypeReference>? typedReferenceSeeds = null,
        IEnumerable<KsProcgenResourceReference>? resourceReferences = null, IEnumerable<KsProcgenCapturedResource>? resources = null,
        int resourceOccurrences = 0, IEnumerable<KsProcgenResourceReference>? resourceReferenceSeeds = null)
    {
        Bodies = Array.AsReadOnly(bodies.ToArray()); Limits = limits;
        Nodes = nodes; Characters = characters;
        ReferenceOccurrences = referenceOccurrences;
        Roots = Array.AsReadOnly((roots ?? Bodies.Where(body => body.Kind is KsProcgenPrototypeBodyKind.Entity or KsProcgenPrototypeBodyKind.Tile)
                .Select(body => new KsProcgenPrototypeRoot(body.Kind, body.PrototypeId)))
            .Distinct().OrderBy(root => root.Kind).ThenBy(root => root.PrototypeId, StringComparer.Ordinal).ToArray());
        EntityReferenceSeeds = Array.AsReadOnly((entityReferenceSeeds ?? []).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToArray());
        DependencyRoots = Array.AsReadOnly((dependencyRoots ?? []).Distinct().OrderBy(root => root.Kind, StringComparer.Ordinal)
            .ThenBy(root => root.PrototypeId, StringComparer.Ordinal).ToArray());
        Dependencies = Array.AsReadOnly((dependencies ?? []).OrderBy(body => body.Kind, StringComparer.Ordinal)
            .ThenBy(body => body.PrototypeId, StringComparer.Ordinal).ToArray());
        AbstractTemplates = Array.AsReadOnly((abstractTemplates ?? []).OrderBy(body => body.Kind, StringComparer.Ordinal)
            .ThenBy(body => body.PrototypeId, StringComparer.Ordinal).ToArray());
        TypedReferenceSeeds = Array.AsReadOnly((typedReferenceSeeds ?? []).Distinct().OrderBy(root => root.Kind, StringComparer.Ordinal)
            .ThenBy(root => root.PrototypeId, StringComparer.Ordinal).ToArray());
        ResourceReferences = Array.AsReadOnly((resourceReferences ?? []).OrderBy(reference => reference.Path, StringComparer.Ordinal)
            .ThenBy(reference => reference.Kind).ThenBy(reference => reference.State, StringComparer.Ordinal).ToArray());
        ResourceReferenceSeeds = Array.AsReadOnly((resourceReferenceSeeds ?? []).Distinct().OrderBy(reference => reference.Path, StringComparer.Ordinal)
            .ThenBy(reference => reference.Kind).ThenBy(reference => reference.State, StringComparer.Ordinal).ToArray());
        Resources = Array.AsReadOnly((resources ?? []).OrderBy(resource => resource.Path, StringComparer.Ordinal).ToArray());
        ResourceOccurrences = resourceOccurrences;
        var text = new StringBuilder("ks-procgen-prototype-bodies-v11;");
        text.Append(Roots.Count.ToString(CultureInfo.InvariantCulture)).Append(';');
        foreach (var root in Roots)
        {
            text.Append(((int) root.Kind).ToString(CultureInfo.InvariantCulture)).Append(';'); Add(root.PrototypeId);
        }
        text.Append(EntityReferenceSeeds.Count.ToString(CultureInfo.InvariantCulture)).Append(';');
        foreach (var id in EntityReferenceSeeds) Add(id);
        text.Append(TypedReferenceSeeds.Count.ToString(CultureInfo.InvariantCulture)).Append(';');
        foreach (var seed in TypedReferenceSeeds) { Add(seed.Kind); Add(seed.PrototypeId); }
        text.Append(Bodies.Count.ToString(CultureInfo.InvariantCulture)).Append(';');
        foreach (var body in Bodies)
        {
            text.Append(((int) body.Kind).ToString(CultureInfo.InvariantCulture)).Append(';');
            Add(body.PrototypeId); text.Append(body.RuntimeTileId.ToString(CultureInfo.InvariantCulture)).Append(';');
            Add(body.CanonicalBody);
            Add(body.CanonicalDeclaration);
            Add(body.CanonicalMetadata);
        }
        text.Append(DependencyRoots.Count.ToString(CultureInfo.InvariantCulture)).Append(';');
        foreach (var root in DependencyRoots) { Add(root.Kind); Add(root.PrototypeId); }
        text.Append(Dependencies.Count.ToString(CultureInfo.InvariantCulture)).Append(';');
        foreach (var dependency in Dependencies)
        {
            Add(dependency.Kind); Add(dependency.PrototypeId); Add(dependency.RuntimeType);
            text.Append(dependency.DeclarationOnly ? '1' : '0');
            Add(dependency.CanonicalBody); Add(dependency.CanonicalDeclaration);
        }
        text.Append(AbstractTemplates.Count.ToString(CultureInfo.InvariantCulture)).Append(';');
        foreach (var template in AbstractTemplates)
        { Add(template.Kind); Add(template.PrototypeId); Add(template.RuntimeType); Add(template.CanonicalFields); Add(template.CanonicalInspection); }
        text.Append(ResourceReferenceSeeds.Count.ToString(CultureInfo.InvariantCulture)).Append(';');
        foreach (var reference in ResourceReferenceSeeds) { Add(((int) reference.Kind).ToString(CultureInfo.InvariantCulture)); Add(reference.Path); Add(reference.State ?? string.Empty); }
        text.Append(ResourceReferences.Count.ToString(CultureInfo.InvariantCulture)).Append(';');
        foreach (var reference in ResourceReferences) { Add(((int) reference.Kind).ToString(CultureInfo.InvariantCulture)); Add(reference.Path); Add(reference.State ?? string.Empty); }
        text.Append(Resources.Count.ToString(CultureInfo.InvariantCulture)).Append(';');
        foreach (var resource in Resources) { Add(resource.Path); Add(resource.Sha256); Add(resource.ByteCount.ToString(CultureInfo.InvariantCulture)); }
        ContentHash = Convert.ToHexString(SHA256.HashData(new UTF8Encoding(false, true).GetBytes(text.ToString())));
        void Add(string value) => text.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value);
    }
    public int SchemaVersion => 11;
    public IReadOnlyList<KsProcgenPrototypeRoot> Roots { get; }
    /// <summary>References from external declarations, distinct from explicit concrete spawn roots.</summary>
    public IReadOnlyList<string> EntityReferenceSeeds { get; }
    public IReadOnlyList<KsProcgenPrototypeBody> Bodies { get; }
    public IReadOnlyList<KsProcgenPrototypeReference> DependencyRoots { get; }
    /// <summary>Typed references in external component overrides; they are not explicit spawn roots.</summary>
    public IReadOnlyList<KsProcgenPrototypeReference> TypedReferenceSeeds { get; }
    public IReadOnlyList<KsProcgenPrototypeDependencyBody> Dependencies { get; }
    /// <summary>Bounded reference-discovery images, never concrete spawn definitions.</summary>
    public IReadOnlyList<KsProcgenPrototypeAbstractTemplate> AbstractTemplates { get; }
    public IReadOnlyList<KsProcgenResourceReference> ResourceReferences { get; }
    public IReadOnlyList<KsProcgenResourceReference> ResourceReferenceSeeds { get; }
    public IReadOnlyList<KsProcgenCapturedResource> Resources { get; }
    public int ResourceOccurrences { get; }
    public int CapturedResourceBytes => Resources.Sum(resource => resource.ByteCount);
    public bool FullContentClosureVerified => false;
    public string ContentHash { get; }
    public int Nodes { get; }
    public int Characters { get; }
    public int ReferenceOccurrences { get; }
    public KsProcgenPrototypeManifestLimits Limits { get; }
}

/// <summary>Captures live resolved prototype data without allocating game entities or retaining mutable data nodes.</summary>
public sealed partial class KsProcgenPrototypeManifestSystem : EntitySystem
{
    [Dependency] private ISerializationManager _serializationManager = default!;
    [Dependency] private ITileDefinitionManager _tileDefinitionManager = default!;

    public bool TryCapture(KsProcgenThemeCatalog catalog, out KsProcgenPrototypeManifest? manifest,
        out KsProcgenIssue? issue, KsProcgenPrototypeManifestLimits? limits = null)
    {
        if (catalog == null)
        { manifest = null; issue = new("InvalidPrototypeManifestInput", "A captured candidate catalog is required."); return false; }
        return TryCapture(CandidateEntityIds(catalog), CandidateTileIds(catalog), out manifest, out issue, limits: limits);
    }

    internal static IEnumerable<string> CandidateEntityIds(KsProcgenThemeCatalog catalog) => catalog.CandidateEntityIds();
    internal static IEnumerable<string> CandidateTileIds(KsProcgenThemeCatalog catalog) => catalog.CandidateTileIds();

    internal static bool TryCanonicalBody(DataNode node, KsProcgenPrototypeManifestLimits limits,
        out string? body, out string? issueCode, out int nodes, out int characters)
    {
        nodes = 0; characters = 0;
        if (!ValidLimits(limits)) { body = null; issueCode = "InvalidPrototypeManifestInput"; return false; }
        var work = new CaptureWork(limits);
        var text = new StringBuilder();
        var success = work.Write(node, text, 0); nodes = work.Nodes; characters = work.Characters;
        if (!success) { body = null; issueCode = work.IssueCode; return false; }
        body = text.ToString(); issueCode = null; return true;
    }

    public bool TryCapture(IEnumerable<string> entityIds, IEnumerable<string> tileIds,
        out KsProcgenPrototypeManifest? manifest, out KsProcgenIssue? issue,
        KsProcgenPrototypeManifestLimits? limits = null, IEnumerable<KsProcgenPrototypeReference>? dependencyRoots = null,
        IEnumerable<KsProcgenPrototypeReference>? typedReferenceSeeds = null,
        IEnumerable<KsProcgenResourceReference>? resourceReferenceSeeds = null)
        => TryCaptureWithReferences(entityIds, tileIds, [], out manifest, out issue, limits: limits, dependencyRoots: dependencyRoots,
            typedReferenceSeeds: typedReferenceSeeds, resourceReferenceSeeds: resourceReferenceSeeds);

    internal bool TryCaptureWithReferences(IEnumerable<string> entityIds, IEnumerable<string> tileIds, IEnumerable<string> entityReferences,
        out KsProcgenPrototypeManifest? manifest, out KsProcgenIssue? issue, KsProcgenPrototypeManifestLimits? limits = null,
        IEnumerable<KsProcgenPrototypeReference>? dependencyRoots = null,
        IEnumerable<KsProcgenPrototypeReference>? typedReferenceSeeds = null,
        IEnumerable<KsProcgenResourceReference>? resourceReferenceSeeds = null)
    {
        manifest = null; issue = null; limits ??= new();
        if (entityIds == null || tileIds == null || entityReferences == null || !ValidLimits(limits))
        { issue = new("InvalidPrototypeManifestInput", "Prototype capture needs bounded references and budgets."); return false; }
        var references = new ReferenceWork(limits);
        foreach (var reference in entityIds.Select(id => (Kind: KsProcgenPrototypeBodyKind.Entity, Id: id))
                     .Concat(tileIds.Select(id => (Kind: KsProcgenPrototypeBodyKind.Tile, Id: id))))
        {
            references.Add(reference.Kind, reference.Id);
            if (references.Issue != null) { issue = references.Issue; return false; }
        }
        var roots = references.Seen.ToHashSet();
        var otherRoots = new List<KsProcgenPrototypeReference>();
        foreach (var root in dependencyRoots ?? [])
        {
            if (root == null || string.IsNullOrWhiteSpace(root.Kind) || root.Kind.Length > 256 ||
                !ProtoMan.TryGetKindType(root.Kind, out var type) ||
                type == typeof(EntityPrototype) || type == typeof(ContentTileDefinition))
            { issue = new("UnsupportedPrototypeManifestKind", "Dependency roots require a registered non-entity, non-tile kind."); return false; }
            references.AddDependency(root.Kind, root.PrototypeId);
            if (references.Issue != null) { issue = references.Issue; return false; }
            otherRoots.Add(root);
        }
        var seeds = new List<string>();
        foreach (var id in entityReferences)
        {
            references.Add(KsProcgenPrototypeBodyKind.Entity, id);
            if (references.Issue != null) { issue = references.Issue; return false; }
            seeds.Add(id);
        }
        var typedSeeds = new List<KsProcgenPrototypeReference>();
        foreach (var seed in typedReferenceSeeds ?? [])
        {
            if (seed == null || string.IsNullOrWhiteSpace(seed.Kind) || seed.Kind.Length > 256 ||
                !ProtoMan.TryGetKindType(seed.Kind, out var type))
            { issue = new("UnsupportedPrototypeManifestKind", "Reference seeds require registered bounded prototype kinds."); return false; }
            if (type == typeof(EntityPrototype)) references.Add(KsProcgenPrototypeBodyKind.Entity, seed.PrototypeId);
            else if (type == typeof(ContentTileDefinition)) references.Add(KsProcgenPrototypeBodyKind.Tile, seed.PrototypeId);
            else references.AddDependency(seed.Kind, seed.PrototypeId);
            if (references.Issue != null) { issue = references.Issue; return false; }
            typedSeeds.Add(seed);
        }
        var resourceWork = new ResourceWork(limits.Resources);
        var resourceSeeds = new List<KsProcgenResourceReference>();
        foreach (var seed in resourceReferenceSeeds ?? [])
        {
            if (seed == null) { issue = new("InvalidPrototypeResourceReference", "Resource seeds require explicit bounded references."); return false; }
            resourceWork.Observe(seed);
            if (resourceWork.Issue != null) { issue = resourceWork.Issue; return false; }
            TryResourcePath(seed.Path, out var canonical);
            resourceSeeds.Add(seed with { Path = canonical! });
        }
        var work = new CaptureWork(limits);
        var context = new PrototypeContext(_serializationManager, entityReference: id =>
        {
            if (!string.IsNullOrEmpty(id)) references.Add(KsProcgenPrototypeBodyKind.Entity, id);
        }, prototypeReference: (type, id) =>
        {
            if (string.IsNullOrEmpty(id) || references.Issue != null) return;
            if (type == typeof(ContentTileDefinition)) references.Add(KsProcgenPrototypeBodyKind.Tile, id);
            else if (ProtoMan.TryGetKindFrom(type, out var kind)) references.AddDependency(kind, id);
            else references.Issue = new("UnsupportedPrototypeManifestKind", "A typed reference uses an unregistered prototype kind.");
        }, resourceReference: resourceWork.Observe);
        var bodies = new List<KsProcgenPrototypeBody>();
        var dependencies = new List<KsProcgenPrototypeDependencyBody>();
        var currentDefinition = string.Empty;
        try
        {
            while (references.Pending.Count > 0 || references.DependencyPending.Count > 0)
            {
                if (resourceWork.Issue != null) { issue = resourceWork.Issue; return false; }
                if (references.Pending.Count == 0)
                {
                    var dependency = references.DependencyPending.Min!;
                    references.DependencyPending.Remove(dependency); currentDefinition = dependency.Kind + "/" + dependency.PrototypeId;
                    if (!TryCaptureDependency(dependency, context, references, work, out var body, out issue)) return false;
                    dependencies.Add(body!);
                    continue;
                }
                var reference = references.Pending.Min; references.Pending.Remove(reference);
                currentDefinition = reference.Id;
                DataNode node;
                DataNode? registeredTileNode = null;
                DataNode? metadataNode = null;
                MappingDataNode? declaration;
                ushort tileId = 0;
                var definitionType = reference.Kind == KsProcgenPrototypeBodyKind.Entity ? typeof(EntityPrototype) : typeof(ContentTileDefinition);
                if (!roots.Contains(reference) && !ProtoMan.TryIndex(definitionType, reference.Id, out _) &&
                    ProtoMan.TryGetMapping(definitionType, reference.Id, out declaration) &&
                    declaration.TryGet<ValueDataNode>("abstract", out var abstractFlag) &&
                    bool.TryParse(abstractFlag.Value, out var abstractDefinition) && abstractDefinition)
                {
                    var entityDeclaration = reference.Kind == KsProcgenPrototypeBodyKind.Entity;
                    if (entityDeclaration && declaration.Has("components") &&
                        (!declaration.TryGet<SequenceDataNode>("components", out var declaredRegistry) || !ValidateRegistry(declaredRegistry, limits, out issue)))
                    { issue ??= new("UnsupportedPrototypeManifestDeclaration", "Abstract references require a bounded component declaration."); return false; }
                    var abstractBody = new StringBuilder();
                    if (!work.Write(declaration, abstractBody, 0, normalizeEntity: entityDeclaration))
                    { issue = new(work.IssueCode!, "Abstract declaration exceeds the shared structural allowance."); return false; }
                    if (!TryObserveAbstract(definitionType, reference.Id, declaration, context, references, work, out _, out issue)) return false;
                    bodies.Add(new(entityDeclaration ? KsProcgenPrototypeBodyKind.EntityDeclaration : KsProcgenPrototypeBodyKind.TileDeclaration,
                        reference.Id, 0, string.Empty, abstractBody.ToString(), string.Empty));
                    continue;
                }
                if (reference.Kind == KsProcgenPrototypeBodyKind.Entity)
                {
                    if (!ProtoMan.TryIndex<EntityPrototype>(reference.Id, out var prototype))
                    { issue = new("MissingPrototypeManifestEntity", "A referenced entity definition is unavailable."); return false; }
                    if (!ProtoMan.TryGetMapping<EntityPrototype>(reference.Id, out declaration))
                    { issue = new("MissingPrototypeManifestDeclaration", "A direct entity definition lacks its resolved source mapping."); return false; }
                    if (!TryObserveInheritance(typeof(EntityPrototype), prototype, declaration, references, out issue)) return false;
                    if (prototype.Components.Count > limits.MaximumComponentsPerEntity)
                    { issue = new("PrototypeManifestComponentBudget", "An entity exceeds the component allowance."); return false; }
                    node = _serializationManager.WriteValue(prototype, alwaysWrite: true, context: context, notNullableOverride: true);
                    // Registry order is a mapping property, unlike ordered sprite layers or variants.
                    if (node is not MappingDataNode mapping || !mapping.TryGet<SequenceDataNode>("components", out var components))
                    { issue = new("UnsupportedPrototypeManifestBody", "Entity serialization must contain its component registry."); return false; }
                    if (!ValidateRegistry(components, limits, out issue)) return false;
                    if (declaration.Has("components") &&
                        (!declaration.TryGet<SequenceDataNode>("components", out var declaredComponents) || !ValidateRegistry(declaredComponents, limits, out issue)))
                    { issue ??= new("UnsupportedPrototypeManifestDeclaration", "Declared component registries require a bounded sequence."); return false; }
                    if (!TryMetadataNode(prototype, limits, context, out metadataNode, out issue)) return false;
                }
                else
                {
                    if (!ProtoMan.TryIndex<ContentTileDefinition>(reference.Id, out var prototype) ||
                        !_tileDefinitionManager.TryGetDefinition(reference.Id, out var tile))
                    { issue = new("MissingPrototypeManifestTile", "A referenced tile definition is unavailable."); return false; }
                    if (tile is not ContentTileDefinition registered || prototype.TileId != registered.TileId ||
                        !_tileDefinitionManager.TryGetDefinition((int) registered.TileId, out var numeric) || !ReferenceEquals(numeric, registered))
                    { issue = new("UnsupportedPrototypeManifestTileBinding", "Loaded, named and numeric tile registrations must agree on their runtime tile ID."); return false; }
                    if (!ProtoMan.TryGetMapping<ContentTileDefinition>(reference.Id, out declaration))
                    { issue = new("MissingPrototypeManifestDeclaration", "A direct tile definition lacks its resolved source mapping."); return false; }
                    if (!TryObserveInheritance(typeof(ContentTileDefinition), prototype, declaration, references, out issue)) return false;
                    tileId = tile.TileId;
                    node = _serializationManager.WriteValue(prototype, alwaysWrite: true, context: context, notNullableOverride: true);
                    // Reloads can replace the prototype instance while the tile manager retains its
                    // original registration. Compare full bodies; matching IDs alone prove nothing.
                    registeredTileNode = _serializationManager.WriteValue(registered, alwaysWrite: true, context: context, notNullableOverride: true);
                }
                if (references.Issue != null) { issue = references.Issue; return false; }
                if (resourceWork.Issue != null) { issue = resourceWork.Issue; return false; }
                var canonical = new StringBuilder();
                var entity = reference.Kind == KsProcgenPrototypeBodyKind.Entity;
                if (!work.Write(node, canonical, 0, normalizeEntity: entity))
                { issue = new(work.IssueCode!, "Prototype body capture exhausted its structural allowance."); return false; }
                if (registeredTileNode != null)
                {
                    var registeredCanonical = new StringBuilder();
                    if (!work.Write(registeredTileNode, registeredCanonical, 0))
                    { issue = new(work.IssueCode!, "Registered tile body comparison exhausted its shared structural allowance."); return false; }
                    if (!StringComparer.Ordinal.Equals(canonical.ToString(), registeredCanonical.ToString()))
                    { issue = new("UnsupportedPrototypeManifestTileBinding", "Loaded and registered tile definitions contain different serialized bodies."); return false; }
                }
                var declared = new StringBuilder();
                if (!work.Write(declaration, declared, 0, normalizeEntity: entity))
                { issue = new(work.IssueCode!, "Resolved declaration capture exhausted its structural allowance."); return false; }
                var metadata = new StringBuilder();
                if (metadataNode != null && !work.Write(metadataNode, metadata, 0))
                { issue = new(work.IssueCode!, "Runtime prototype metadata exhausted its shared structural allowance."); return false; }
                bodies.Add(new(reference.Kind, reference.Id, tileId, canonical.ToString(), declared.ToString(), metadata.ToString()));
            }
            if (!TryCaptureResources(resourceWork, limits.Resources, out var resourceFiles, out issue)) return false;
            manifest = new(bodies.OrderBy(body => body.Kind).ThenBy(body => body.PrototypeId, StringComparer.Ordinal), limits,
                work.Nodes, work.Characters, references.Occurrences, roots: roots.Select(root => new KsProcgenPrototypeRoot(root.Kind, root.Id)), entityReferenceSeeds: seeds,
                dependencyRoots: otherRoots, dependencies: dependencies, abstractTemplates: references.AbstractTemplates,
                typedReferenceSeeds: typedSeeds, resourceReferences: resourceWork.References, resources: resourceFiles,
                resourceOccurrences: resourceWork.Occurrences, resourceReferenceSeeds: resourceSeeds);
            return true;
        }
        catch (Exception exception)
        {
            Log.Debug($"Prototype manifest capture failed: {exception}");
            issue = new("UnsupportedPrototypeManifestBody",
                $"Prototype {currentDefinition} could not be serialized: {exception.GetType().Name}: {exception.Message}"); return false;
        }
    }

    /// <summary>Fresh ordinal body comparison, not hash equality alone or proof of instance state.</summary>
    public bool Matches(KsProcgenPrototypeManifest manifest, out KsProcgenIssue? issue)
    {
        if (!TryCaptureWithReferences(manifest.Roots.Where(root => root.Kind == KsProcgenPrototypeBodyKind.Entity).Select(root => root.PrototypeId),
                manifest.Roots.Where(root => root.Kind == KsProcgenPrototypeBodyKind.Tile).Select(root => root.PrototypeId),
                manifest.EntityReferenceSeeds,
                out var fresh, out issue, limits: manifest.Limits, dependencyRoots: manifest.DependencyRoots,
                typedReferenceSeeds: manifest.TypedReferenceSeeds, resourceReferenceSeeds: manifest.ResourceReferenceSeeds)) return CaptureDrift(ref issue);
        if (manifest.Roots.SequenceEqual(fresh!.Roots) && manifest.EntityReferenceSeeds.SequenceEqual(fresh.EntityReferenceSeeds) &&
            manifest.Bodies.SequenceEqual(fresh.Bodies) && manifest.DependencyRoots.SequenceEqual(fresh.DependencyRoots) &&
            manifest.Dependencies.SequenceEqual(fresh.Dependencies) && manifest.AbstractTemplates.SequenceEqual(fresh.AbstractTemplates) &&
            manifest.TypedReferenceSeeds.SequenceEqual(fresh.TypedReferenceSeeds) &&
            manifest.ResourceReferences.SequenceEqual(fresh.ResourceReferences) && manifest.ResourceReferenceSeeds.SequenceEqual(fresh.ResourceReferenceSeeds) &&
            manifest.Resources.Count == fresh.Resources.Count && manifest.Resources.Zip(fresh.Resources).All(pair => pair.First.SameBytes(pair.Second))) return true;
        issue = new("PrototypeManifestChanged", "Resolved referenced prototype bodies changed since capture.");
        return false;
    }

    internal bool MatchesBody(KsProcgenPrototypeManifest manifest, KsProcgenPrototypeBodyKind kind,
        string prototypeId, out KsProcgenIssue? issue)
    {
        if (kind != KsProcgenPrototypeBodyKind.Entity && kind != KsProcgenPrototypeBodyKind.Tile)
        { issue = new("UnsupportedPrototypeManifestPlacementKind", "Only concrete entity and tile bodies can authorize placement."); return false; }
        var expected = manifest.Bodies.FirstOrDefault(body => body.Kind == kind && body.PrototypeId == prototypeId);
        if (expected == null)
        { issue = new("UncapturedPrototypeReference", "A write references a definition outside the captured candidate pool."); return false; }
        var ids = new[] { prototypeId };
        if (!TryCapture(kind == KsProcgenPrototypeBodyKind.Entity ? ids : [],
                kind == KsProcgenPrototypeBodyKind.Tile ? ids : [], out var fresh, out issue, limits: manifest.Limits)) return CaptureDrift(ref issue);
        var retainedBodies = manifest.Bodies.ToDictionary(body => (body.Kind, body.PrototypeId));
        var retainedDependencies = manifest.Dependencies.ToDictionary(body => new KsProcgenPrototypeReference(body.Kind, body.PrototypeId));
        var retainedTemplates = manifest.AbstractTemplates.ToDictionary(body => new KsProcgenPrototypeReference(body.Kind, body.PrototypeId));
        var retainedResources = manifest.Resources.ToDictionary(resource => resource.Path, StringComparer.Ordinal);
        if (fresh!.Bodies.All(body => retainedBodies.TryGetValue((body.Kind, body.PrototypeId), out var retained) && retained == body) &&
            fresh.Dependencies.All(body => retainedDependencies.TryGetValue(new(body.Kind, body.PrototypeId), out var retained) && retained == body) &&
            fresh.AbstractTemplates.All(body => retainedTemplates.TryGetValue(new(body.Kind, body.PrototypeId), out var retained) && retained == body) &&
            fresh.ResourceReferences.All(reference => manifest.ResourceReferences.Contains(reference)) &&
            fresh.Resources.All(resource => retainedResources.TryGetValue(resource.Path, out var retained) && retained.SameBytes(resource))) return true;
        issue = new("PrototypeManifestChanged", "A referenced prototype body changed before placement.");
        return false;
    }

    internal static bool ValidLimits(KsProcgenPrototypeManifestLimits limits) =>
        limits.MaximumDefinitions is >= 1 and <= 4096 && limits.MaximumReferences is >= 1 and <= 131_072 &&
        limits.MaximumComponentsPerEntity is >= 1 and <= 512 && limits.MaximumNodes is >= 1 and <= 1_048_576 &&
        limits.MaximumCategoriesPerEntity is >= 0 and <= 256 &&
        limits.MaximumCharacters is >= 1 and <= 16_777_216 && limits.MaximumDepth is >= 1 and <= 128 && limits.Resources != null && ValidResourceLimits(limits.Resources);

    private static bool CaptureDrift(ref KsProcgenIssue? issue)
    {
        if (issue?.Code == "UnsupportedPrototypeManifestTileBinding")
            issue = new("PrototypeManifestChanged", "A captured tile's prototype and engine registration no longer agree.");
        else if (issue?.Code is "MissingPrototypeResource" or "MissingPrototypeRsiState" or
                 "InvalidPrototypeResource" or "InvalidPrototypeRsi" or "InvalidPrototypeResourceReference")
            issue = new("PrototypeManifestChanged", $"Captured content can no longer be read: {issue.Code}.");
        return false;
    }

    private static bool ValidateRegistry(SequenceDataNode components, KsProcgenPrototypeManifestLimits limits, out KsProcgenIssue? issue)
    {
        issue = null;
        if (components.Count > limits.MaximumComponentsPerEntity)
        { issue = new("PrototypeManifestComponentBudget", "A declared or resolved registry exceeds the component allowance."); return false; }
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in components)
            if (entry is not MappingDataNode component || !component.TryGet<ValueDataNode>("type", out var type) ||
                string.IsNullOrWhiteSpace(type.Value) || type.Value.Length > 256 || !names.Add(type.Value))
            { issue = new("UnsupportedPrototypeManifestDeclaration", "Component names must be bounded and unique."); return false; }
        return true;
    }

    private sealed class CaptureWork(KsProcgenPrototypeManifestLimits limits)
    {
        public int Nodes;
        public int Characters;
        public string? IssueCode;
        private bool Add(StringBuilder text, string value)
        {
            if ((long) Characters + value.Length > limits.MaximumCharacters)
            { IssueCode = "PrototypeManifestCharacterBudget"; return false; }
            Characters += value.Length; text.Append(value); return true;
        }
        private bool Text(StringBuilder text, string? value) => value == null ? Add(text, "N;") :
            Add(text, value.Length.ToString(CultureInfo.InvariantCulture) + ":") && Add(text, value);
        public bool Write(DataNode node, StringBuilder text, int depth, bool normalizeEntity = false)
        {
            if (depth > limits.MaximumDepth) { IssueCode = "PrototypeManifestDepthBudget"; return false; }
            if (Nodes >= limits.MaximumNodes) { IssueCode = "PrototypeManifestNodeBudget"; return false; }
            Nodes++;
            if (!Text(text, node.Tag) || !Add(text, node.IsNull ? "1" : "0")) return false;
            switch (node)
            {
                case ValueDataNode value: return Add(text, "V") && Text(text, value.Value);
                case SequenceDataNode sequence:
                    if (sequence.Count > limits.MaximumNodes - Nodes) { IssueCode = "PrototypeManifestNodeBudget"; return false; }
                    if (!Add(text, "S" + sequence.Count.ToString(CultureInfo.InvariantCulture) + ":")) return false;
                    foreach (var child in sequence) if (!Write(child, text, depth + 1)) return false;
                    return true;
                case MappingDataNode mapping:
                    if (mapping.Count > limits.MaximumNodes - Nodes) { IssueCode = "PrototypeManifestNodeBudget"; return false; }
                    if (!Add(text, "M" + mapping.Count.ToString(CultureInfo.InvariantCulture) + ":")) return false;
                    foreach (var (key, value) in mapping.Children.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                    {
                        DataNode child = value;
                        if (normalizeEntity && key == "components" && value is SequenceDataNode registry)
                            child = new SequenceDataNode(registry.Cast<MappingDataNode>()
                                .OrderBy(component => ((ValueDataNode) component["type"]).Value, StringComparer.Ordinal).Cast<DataNode>().ToArray())
                            { Tag = registry.Tag, IsNull = registry.IsNull };
                        if (!Text(text, mapping.GetKeyTag(key)) || !Text(text, key) || !Write(child, text, depth + 1)) return false;
                    }
                    return true;
                default: IssueCode = "UnsupportedPrototypeManifestBody"; return false;
            }
        }
    }
}
