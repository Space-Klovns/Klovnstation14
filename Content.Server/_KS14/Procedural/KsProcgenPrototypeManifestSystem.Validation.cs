using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Markdown.Validation;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Serialization.TypeSerializers.Interfaces;

namespace Content.Server._KS14.Procedural;

public sealed partial class KsProcgenPrototypeManifestSystem
{
    private sealed class ComponentEntityReferenceValidator<T> : ITypeValidator<EntProtoId<T>, ValueDataNode>
        where T : IComponent, new()
    {
        public ComponentEntityReferenceValidator() { }
        public ValidationNode Validate(ISerializationManager serializationManager, ValueDataNode node,
            IDependencyCollection dependencies, ISerializationContext? context = null)
        {
            if (string.IsNullOrWhiteSpace(node.Value)) return new ErrorNode(node, "Declared entity references require nonempty IDs.");
            (context as PrototypeContext)?.ObserveEntityReference(node.Value);
            // The bounded closure queue verifies existence, including legitimate abstract IDs.
            return new ValidatedValueNode(node);
        }
    }

    private sealed class TypedPrototypeReferenceValidator<T> : ITypeValidator<ProtoId<T>, ValueDataNode>
        where T : class, IPrototype
    {
        public TypedPrototypeReferenceValidator() { }
        public ValidationNode Validate(ISerializationManager serializationManager, ValueDataNode node,
            IDependencyCollection dependencies, ISerializationContext? context = null)
        {
            if (string.IsNullOrWhiteSpace(node.Value)) return new ErrorNode(node, "Declared prototype references require nonempty IDs.");
            (context as PrototypeContext)?.ObservePrototypeReference(typeof(T), node.Value);
            return new ValidatedValueNode(node);
        }
    }
}
