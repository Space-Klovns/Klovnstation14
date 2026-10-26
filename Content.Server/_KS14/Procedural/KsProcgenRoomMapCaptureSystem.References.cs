using System.Globalization;
using System.Threading;
using Content.Shared._KS14.Procedural;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Validation;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Serialization.TypeSerializers.Interfaces;

namespace Content.Server._KS14.Procedural;

public sealed partial class KsProcgenRoomMapCaptureSystem
{
    private bool TryInspectComponentReferences(int owner, MappingDataNode component,
        List<KsProcgenMapEntityReference> references, List<string> prototypes, List<KsProcgenPrototypeReference> typedPrototypes, List<KsProcgenResourceReference> resources, List<string> uninspected,
        EntityWork work, CancellationToken cancellationToken, out KsProcgenIssue? issue)
    {
        issue = null;
        cancellationToken.ThrowIfCancellationRequested();
        if (!work.Spend()) return EntityBudget(out issue);
        var name = component.Get<ValueDataNode>("type").Value;
        // Chunk payloads have their own decoder. Ignored client components require a client schema;
        // retain their opacity instead of guessing reference fields from numeric scalar values.
        if (name == "MapGrid" || !Factory.TryGetRegistration(name, out var registration))
        { uninspected.Add(name); return true; }
        var fields = new MappingDataNode { Tag = component.Tag };
        foreach (var (key, value) in component.Children)
        {
            if (key == "type") continue;
            fields.Add(key, value);
            if (component.GetKeyTag(key) is { } tag) fields.SetKeyTag(key, tag);
        }
        var context = new SourceReferenceContext(_serialization, ProtoMan, name, references, prototypes, typedPrototypes, resources, work, cancellationToken);
        try
        {
            var validation = _serialization.ValidateNode(registration.Type, fields, context: context);
            if (!validation.Valid) uninspected.Add(name);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            Log.Debug($"Cannot inspect source component {owner}/{name}: {exception}");
            uninspected.Add(name);
        }
        issue = context.Issue;
        return issue == null;
    }

    private static bool CheckSelectedReferences(SourceEntity entity, int gridUid, Dictionary<int, bool> selected,
        Dictionary<int, SourceEntity> sources, EntityWork work, out KsProcgenIssue? issue)
    {
        issue = null;
        foreach (var reference in entity.References)
        {
            if (!work.Spend()) return EntityBudget(out issue);
            if (!sources.TryGetValue(reference.TargetYamlUid, out var target))
                return Reject("MissingRoomMapComponentReference", $"Entity {entity.Uid}/{reference.ComponentType} references absent source UID {reference.TargetYamlUid}.", out issue);
            if (reference.Kind == KsProcgenMapReferenceKind.Map)
            {
                var ancestor = sources[gridUid];
                while (!ancestor.IsMap && ancestor.Parent != 0)
                {
                    if (!work.Spend()) return EntityBudget(out issue);
                    ancestor = sources[ancestor.Parent];
                }
                if (!target.IsMap || target.Uid != ancestor.Uid)
                    return Reject("RoomMapExtractionExternalMapReference", "A map reference must name the selected grid's owning source map.", out issue);
                continue;
            }
            if (target.Uid == gridUid || selected.GetValueOrDefault(target.Uid)) continue;
            return Reject("RoomMapExtractionCutsComponentReference", $"Entity {entity.Uid}/{reference.ComponentType} references UID {target.Uid} outside the selected room.", out issue);
        }
        return true;
    }

    /// <summary>Validation observes symbolic IDs without allocating entities, fabricating world IDs or running load hooks.</summary>
    private sealed class SourceReferenceContext : ISerializationContext, IKsProcgenResourceObservationContext,
        ITypeValidator<EntityUid, ValueDataNode>, ITypeValidator<NetEntity, ValueDataNode>,
        ITypeValidator<MapId, ValueDataNode>, ITypeValidator<EntProtoId, ValueDataNode>
    {
        private readonly string _component;
        private readonly List<KsProcgenMapEntityReference> _references;
        private readonly List<string> _prototypes;
        private readonly List<KsProcgenPrototypeReference> _typedPrototypes;
        private readonly List<KsProcgenResourceReference> _resources;
        private readonly IPrototypeManager _prototypeManager;
        private readonly EntityWork _work;
        private readonly CancellationToken _cancellationToken;
        public KsProcgenIssue? Issue;
        public SourceReferenceContext(ISerializationManager serialization, IPrototypeManager prototypeManager, string component,
            List<KsProcgenMapEntityReference> references, List<string> prototypes, List<KsProcgenPrototypeReference> typedPrototypes, List<KsProcgenResourceReference> resources,
            EntityWork work, CancellationToken cancellationToken)
        {
            _component = component; _references = references; _prototypes = prototypes; _work = work; _cancellationToken = cancellationToken;
            _typedPrototypes = typedPrototypes; _prototypeManager = prototypeManager; _resources = resources;
            SerializerProvider = new(serialization);
            SerializerProvider.RegisterSerializer(this);
            SerializerProvider.RegisterSerializer(typeof(SourceComponentPrototypeValidator<>));
            SerializerProvider.RegisterSerializer(typeof(SourceTypedPrototypeValidator<>));
            KsProcgenPrototypeManifestSystem.RegisterResourceObservers(SerializerProvider);
        }
        public SerializationManager.SerializerProvider SerializerProvider { get; }
        public bool WritingReadingPrototypes => false;
        private bool Spend()
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (Issue != null) return false;
            if (_work.Spend()) return true;
            Issue = new("RoomEntityExtractionGraphBudget", "Component reference inspection exhausted the shared graph allowance.");
            return false;
        }
        public void ObserveResource(KsProcgenResourceReference reference) { if (Spend()) _resources.Add(reference); }
        public void ObserveSpriteEntity(string id) => ObservePrototype(id);
        public void ObservePrototype(string id)
        {
            if (Spend()) _prototypes.Add(id);
        }
        public void ObservePrototype(Type type, string id)
        {
            if (type == typeof(EntityPrototype)) { ObservePrototype(id); return; }
            if (!Spend()) return;
            if (!_prototypeManager.TryGetKindFrom(type, out var kind))
            { Issue = new("UnsupportedRoomMapPrototypeKind", "A typed source reference requires a registered prototype kind."); return; }
            _typedPrototypes.Add(new(kind, id));
        }
        private ValidationNode Observe(ValueDataNode node, KsProcgenMapReferenceKind kind)
        {
            if (!Spend()) return new ErrorNode(node, "Source reference work exhausted.");
            if (!node.IsNull && node.Value == "invalid") return new ValidatedValueNode(node);
            if (node.IsNull || !int.TryParse(node.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var uid) || uid < 1)
            {
                Issue = new("InvalidRoomMapComponentReference", $"Component {_component} requires invalid or positive map-local IDs.");
                return new ErrorNode(node, Issue.Message);
            }
            _references.Add(new(_component, kind, uid));
            return new ValidatedValueNode(node);
        }
        ValidationNode ITypeValidator<EntityUid, ValueDataNode>.Validate(ISerializationManager serializationManager,
            ValueDataNode node, IDependencyCollection dependencies, ISerializationContext? context) => Observe(node, KsProcgenMapReferenceKind.Entity);
        ValidationNode ITypeValidator<NetEntity, ValueDataNode>.Validate(ISerializationManager serializationManager,
            ValueDataNode node, IDependencyCollection dependencies, ISerializationContext? context) => Observe(node, KsProcgenMapReferenceKind.NetworkEntity);
        ValidationNode ITypeValidator<MapId, ValueDataNode>.Validate(ISerializationManager serializationManager,
            ValueDataNode node, IDependencyCollection dependencies, ISerializationContext? context) => Observe(node, KsProcgenMapReferenceKind.Map);
        ValidationNode ITypeValidator<EntProtoId, ValueDataNode>.Validate(ISerializationManager serializationManager,
            ValueDataNode node, IDependencyCollection dependencies, ISerializationContext? context)
        {
            ObservePrototype(node.Value);
            return serializationManager.ValidateNode<EntProtoId>(node);
        }
    }

    private sealed class SourceComponentPrototypeValidator<T> : ITypeValidator<EntProtoId<T>, ValueDataNode> where T : IComponent, new()
    {
        public SourceComponentPrototypeValidator() { }
        public ValidationNode Validate(ISerializationManager serializationManager, ValueDataNode node,
            IDependencyCollection dependencies, ISerializationContext? context = null)
        {
            (context as SourceReferenceContext)?.ObservePrototype(node.Value);
            return serializationManager.ValidateNode<EntProtoId<T>>(node);
        }
    }

    private sealed class SourceTypedPrototypeValidator<T> : ITypeValidator<ProtoId<T>, ValueDataNode> where T : class, IPrototype
    {
        public SourceTypedPrototypeValidator() { }
        public ValidationNode Validate(ISerializationManager serializationManager, ValueDataNode node,
            IDependencyCollection dependencies, ISerializationContext? context = null)
        {
            (context as SourceReferenceContext)?.ObservePrototype(typeof(T), node.Value);
            return serializationManager.ValidateNode<ProtoId<T>>(node);
        }
    }
}
