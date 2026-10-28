using Robust.Shared.IoC;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Validation;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Serialization.TypeSerializers.Interfaces;
using Robust.Shared.Utility;

namespace Content.Server._KS14.Procedural;

public sealed partial class KsProcgenPrototypeManifestSystem
{
    internal static void RegisterResourceObservers(SerializationManager.SerializerProvider provider)
    {
        provider.RegisterSerializer<ResourcePathObserver>();
        provider.RegisterSerializer<SpriteResourceObserver>();
    }

    private sealed class ResourcePathObserver : ITypeWriter<ResPath>, ITypeValidator<ResPath, ValueDataNode>
    {
        public ResourcePathObserver() { }
        public DataNode Write(ISerializationManager serializationManager, ResPath value, IDependencyCollection dependencies,
            bool alwaysWrite = false, ISerializationContext? context = null)
        {
            Observe(value.ToString(), context);
            return new ValueDataNode(value.ToString());
        }
        public ValidationNode Validate(ISerializationManager serializationManager, ValueDataNode node,
            IDependencyCollection dependencies, ISerializationContext? context = null)
        {
            Observe(node.Value, context);
            return new ValidatedValueNode(node);
        }
        private static void Observe(string path, ISerializationContext? context)
        {
            if (string.IsNullOrEmpty(path) || path == ".") return;
            var kind = path.EndsWith(".rsi", StringComparison.OrdinalIgnoreCase)
                ? KsProcgenResourceReferenceKind.Rsi : KsProcgenResourceReferenceKind.Path;
            (context as IKsProcgenResourceObservationContext)?.ObserveResource(new(kind, path));
        }
    }

    private sealed class SpriteResourceObserver : ITypeWriter<SpriteSpecifier>, ITypeWriter<SpriteSpecifier.Rsi>,
        ITypeWriter<SpriteSpecifier.Texture>, ITypeWriter<SpriteSpecifier.EntityPrototype>,
        ITypeValidator<SpriteSpecifier, ValueDataNode>, ITypeValidator<SpriteSpecifier, MappingDataNode>,
        ITypeValidator<SpriteSpecifier.Texture, ValueDataNode>, ITypeValidator<SpriteSpecifier.Rsi, MappingDataNode>,
        ITypeValidator<SpriteSpecifier.EntityPrototype, ValueDataNode>
    {
        public SpriteResourceObserver() { }
        private static string TexturePath(string path) => (new ResPath("/Textures") / new ResPath(path)).ToString();
        public DataNode Write(ISerializationManager serializationManager, SpriteSpecifier value, IDependencyCollection dependencies,
            bool alwaysWrite = false, ISerializationContext? context = null) => value switch
        {
            SpriteSpecifier.Rsi sprite => Write(serializationManager, sprite, dependencies, alwaysWrite: alwaysWrite, context: context),
            SpriteSpecifier.Texture texture => Write(serializationManager, texture, dependencies, alwaysWrite: alwaysWrite, context: context),
            SpriteSpecifier.EntityPrototype entity => Write(serializationManager, entity, dependencies, alwaysWrite: alwaysWrite, context: context),
            _ => throw new InvalidOperationException("Unsupported sprite reference kind.")
        };
        public DataNode Write(ISerializationManager serializationManager, SpriteSpecifier.Rsi value, IDependencyCollection dependencies,
            bool alwaysWrite = false, ISerializationContext? context = null)
        {
            (context as IKsProcgenResourceObservationContext)?.ObserveResource(new(KsProcgenResourceReferenceKind.Rsi, TexturePath(value.RsiPath.ToString()), State: value.RsiState));
            return new MappingDataNode { { "sprite", new ValueDataNode(value.RsiPath.ToString()) }, { "state", new ValueDataNode(value.RsiState) } };
        }
        public DataNode Write(ISerializationManager serializationManager, SpriteSpecifier.Texture value, IDependencyCollection dependencies,
            bool alwaysWrite = false, ISerializationContext? context = null)
        {
            if (value.TexturePath != ResPath.Self && !string.IsNullOrEmpty(value.TexturePath.ToString()))
                (context as IKsProcgenResourceObservationContext)?.ObserveResource(new(KsProcgenResourceReferenceKind.Path, TexturePath(value.TexturePath.ToString())));
            return new ValueDataNode(value.TexturePath.ToString());
        }
        public DataNode Write(ISerializationManager serializationManager, SpriteSpecifier.EntityPrototype value, IDependencyCollection dependencies,
            bool alwaysWrite = false, ISerializationContext? context = null)
        {
            if (string.IsNullOrWhiteSpace(value.EntityPrototypeId))
                throw new InvalidOperationException("Entity-based sprites require a nonempty prototype ID.");
            (context as IKsProcgenResourceObservationContext)?.ObserveSpriteEntity(value.EntityPrototypeId);
            return new MappingDataNode { { "entity", new ValueDataNode(value.EntityPrototypeId) } };
        }
        public ValidationNode Validate(ISerializationManager serializationManager, ValueDataNode node,
            IDependencyCollection dependencies, ISerializationContext? context = null)
        {
            if (!node.IsNull && node.Value is not "" and not ".")
                (context as IKsProcgenResourceObservationContext)?.ObserveResource(new(KsProcgenResourceReferenceKind.Path, TexturePath(node.Value)));
            return new ValidatedValueNode(node);
        }
        public ValidationNode Validate(ISerializationManager serializationManager, MappingDataNode node,
            IDependencyCollection dependencies, ISerializationContext? context = null)
        {
            if (node.TryGet<ValueDataNode>("entity", out var entity))
            {
                if (entity.IsNull || string.IsNullOrWhiteSpace(entity.Value))
                    return new ErrorNode(node, "Entity-based sprites require a nonempty prototype ID.");
                (context as IKsProcgenResourceObservationContext)?.ObserveSpriteEntity(entity.Value);
                return new ValidatedValueNode(node);
            }
            if (!node.TryGet<ValueDataNode>("sprite", out var path) || !node.TryGet<ValueDataNode>("state", out var state))
                return new ErrorNode(node, "RSI references need scalar sprite and state fields.");
            (context as IKsProcgenResourceObservationContext)?.ObserveResource(new(KsProcgenResourceReferenceKind.Rsi, TexturePath(path.Value), State: state.Value));
            return new ValidatedValueNode(node);
        }
        ValidationNode ITypeValidator<SpriteSpecifier.EntityPrototype, ValueDataNode>.Validate(ISerializationManager serializationManager,
            ValueDataNode node, IDependencyCollection dependencies, ISerializationContext? context)
        {
            if (node.IsNull || string.IsNullOrWhiteSpace(node.Value))
                return new ErrorNode(node, "Entity-based sprites require a nonempty prototype ID.");
            (context as IKsProcgenResourceObservationContext)?.ObserveSpriteEntity(node.Value);
            return new ValidatedValueNode(node);
        }
    }
}
