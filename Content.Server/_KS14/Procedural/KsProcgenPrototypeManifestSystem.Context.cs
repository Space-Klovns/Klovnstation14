using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Serialization.Markdown.Validation;
using Robust.Shared.Serialization.TypeSerializers.Interfaces;
using Robust.Shared.Serialization.TypeSerializers.Implementations;

namespace Content.Server._KS14.Procedural;

public sealed partial class KsProcgenPrototypeManifestSystem
{
    private sealed class PrototypeWorldReferenceErrorNode(ValueDataNode node)
        : ErrorNode(node, "Captured prototype references must be invalid world defaults.")
    {
    }

    /// <summary>Prototype data can contain invalid entity defaults, never references into a live world.</summary>
    private sealed class PrototypeContext : IKsPrototypeDataContext, IKsProcgenResourceObservationContext,
        ITypeWriter<EntityUid>, ITypeWriter<NetEntity>, ITypeWriter<MapId>, ITypeWriter<EntProtoId>,
        ITypeReader<EntityUid, ValueDataNode>, ITypeReader<NetEntity, ValueDataNode>, ITypeReader<MapId, ValueDataNode>, ITypeValidator<EntProtoId, ValueDataNode>
    {
        private readonly Action<string?>? _entityReference;
        private readonly Action<Type, string?>? _prototypeReference;
        private readonly Action<KsProcgenResourceReference>? _resourceReference;
        public PrototypeContext(ISerializationManager serializationManager, Action<string?>? entityReference = null,
            Action<Type, string?>? prototypeReference = null, Action<KsProcgenResourceReference>? resourceReference = null)
        {
            _entityReference = entityReference;
            _prototypeReference = prototypeReference;
            _resourceReference = resourceReference;
            SerializerProvider = new(serializationManager);
            SerializerProvider.RegisterSerializer(this);
            // A private registry serializer prevents prototype loader component interning from
            // sharing mutable component instances with decoded placement blueprints.
            SerializerProvider.RegisterSerializer<ComponentRegistrySerializer>();
            if (resourceReference != null) SerializerProvider.RegisterSerializer<ResourcePathObserver>();
            if (entityReference != null || prototypeReference != null || resourceReference != null)
            {
                SerializerProvider.RegisterSerializer(typeof(ComponentEntityReferenceWriter<>));
                SerializerProvider.RegisterSerializer(typeof(TypedPrototypeReferenceWriter<>));
                SerializerProvider.RegisterSerializer(typeof(ComponentEntityReferenceValidator<>));
                SerializerProvider.RegisterSerializer(typeof(TypedPrototypeReferenceValidator<>));
                SerializerProvider.RegisterSerializer<SpriteResourceObserver>();
            }
        }
        public SerializationManager.SerializerProvider SerializerProvider { get; }
        public bool WritingReadingPrototypes => true;
        public bool IncludeReadOnlyFields => true;
        public void ObserveEntityReference(string? id) => _entityReference?.Invoke(id);
        public void ObserveResource(KsProcgenResourceReference reference) => _resourceReference?.Invoke(reference);
        public void ObserveSpriteEntity(string id) => ObserveEntityReference(id);
        public void ObservePrototypeReference(Type type, string? id)
        {
            if (type == typeof(EntityPrototype)) ObserveEntityReference(id);
            else _prototypeReference?.Invoke(type, id);
        }
        public DataNode Write(ISerializationManager serializationManager, EntProtoId value,
            IDependencyCollection dependencies, bool alwaysWrite = false, ISerializationContext? context = null)
        {
            ObserveEntityReference(value.Id);
            return new ValueDataNode(value.Id);
        }
        ValidationNode ITypeValidator<EntProtoId, ValueDataNode>.Validate(ISerializationManager serializationManager,
            ValueDataNode node, IDependencyCollection dependencies, ISerializationContext? context)
        {
            if (string.IsNullOrWhiteSpace(node.Value)) return new ErrorNode(node, "Declared entity references require nonempty IDs.");
            if (_entityReference == null) return serializationManager.ValidateNode<EntProtoId>(node);
            ObserveEntityReference(node.Value);
            return new ValidatedValueNode(node);
        }
        ValidationNode ITypeValidator<EntityUid, ValueDataNode>.Validate(ISerializationManager serializationManager,
            ValueDataNode node, IDependencyCollection dependencies, ISerializationContext? context)
            => ValidateDefault(node, "invalid");
        ValidationNode ITypeValidator<NetEntity, ValueDataNode>.Validate(ISerializationManager serializationManager,
            ValueDataNode node, IDependencyCollection dependencies, ISerializationContext? context)
            => ValidateDefault(node, "invalid");
        ValidationNode ITypeValidator<MapId, ValueDataNode>.Validate(ISerializationManager serializationManager,
            ValueDataNode node, IDependencyCollection dependencies, ISerializationContext? context)
            => ValidateDefault(node, "nullspace");
        private static ValidationNode ValidateDefault(ValueDataNode node, string expected) =>
            !node.IsNull && node.Value == expected ? new ValidatedValueNode(node) :
                new PrototypeWorldReferenceErrorNode(node);
        EntityUid ITypeReader<EntityUid, ValueDataNode>.Read(ISerializationManager serializationManager, ValueDataNode node,
            IDependencyCollection dependencies, SerializationHookContext hookCtx, ISerializationContext? context,
            ISerializationManager.InstantiationDelegate<EntityUid>? instanceProvider)
            => node.Value == "invalid" ? EntityUid.Invalid : throw new InvalidOperationException("Snapshot contains a world entity reference.");
        NetEntity ITypeReader<NetEntity, ValueDataNode>.Read(ISerializationManager serializationManager, ValueDataNode node,
            IDependencyCollection dependencies, SerializationHookContext hookCtx, ISerializationContext? context,
            ISerializationManager.InstantiationDelegate<NetEntity>? instanceProvider)
            => node.Value == "invalid" ? NetEntity.Invalid : throw new InvalidOperationException("Snapshot contains a network entity reference.");
        MapId ITypeReader<MapId, ValueDataNode>.Read(ISerializationManager serializationManager, ValueDataNode node,
            IDependencyCollection dependencies, SerializationHookContext hookCtx, ISerializationContext? context,
            ISerializationManager.InstantiationDelegate<MapId>? instanceProvider)
            => node.Value == "nullspace" ? MapId.Nullspace : throw new InvalidOperationException("Snapshot contains a world map reference.");
        public DataNode Write(ISerializationManager serializationManager, EntityUid value,
            IDependencyCollection dependencies, bool alwaysWrite = false, ISerializationContext? context = null)
        {
            if (value != EntityUid.Invalid) throw new InvalidOperationException("Prototype data contains a live entity reference.");
            return new ValueDataNode("invalid");
        }
        public DataNode Write(ISerializationManager serializationManager, NetEntity value,
            IDependencyCollection dependencies, bool alwaysWrite = false, ISerializationContext? context = null)
        {
            if (value != NetEntity.Invalid) throw new InvalidOperationException("Prototype data contains a live network entity reference.");
            return new ValueDataNode("invalid");
        }
        public DataNode Write(ISerializationManager serializationManager, MapId value,
            IDependencyCollection dependencies, bool alwaysWrite = false, ISerializationContext? context = null)
        {
            if (value != MapId.Nullspace) throw new InvalidOperationException("Prototype data contains a live map reference.");
            return new ValueDataNode("nullspace");
        }
    }
}
