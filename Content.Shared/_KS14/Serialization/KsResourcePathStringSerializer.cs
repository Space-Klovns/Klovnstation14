using Robust.Shared.IoC;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Validation;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Serialization.TypeSerializers.Interfaces;
using Robust.Shared.Utility;

namespace Content.Shared._KS14.Serialization;

/// <summary>
/// Marks an existing string data field as a resource path without changing its public or YAML
/// representation. Delegation preserves contextual resource inspection and native path validation.
/// Apply explicitly to resource fields; this must never become the default string serializer.
/// </summary>
public sealed class KsResourcePathStringSerializer : ITypeSerializer<string, ValueDataNode>
{
    public string Read(ISerializationManager serializationManager, ValueDataNode node,
        IDependencyCollection dependencies, SerializationHookContext hookCtx,
        ISerializationContext? context = null,
        ISerializationManager.InstantiationDelegate<string>? instanceProvider = null) => node.Value;

    public ValidationNode Validate(ISerializationManager serializationManager, ValueDataNode node,
        IDependencyCollection dependencies, ISerializationContext? context = null)
    {
        if (string.IsNullOrWhiteSpace(node.Value) || node.Value == ".")
            return new ErrorNode(node, "Resource path strings require an explicit content path.");
        return serializationManager.ValidateNode<ResPath>(node, context: context);
    }

    public DataNode Write(ISerializationManager serializationManager, string value,
        IDependencyCollection dependencies, bool alwaysWrite = false, ISerializationContext? context = null)
    {
        if (string.IsNullOrWhiteSpace(value) || value == ".")
            throw new InvalidOperationException("Resource path strings require an explicit content path.");
        return serializationManager.WriteValue(new ResPath(value), alwaysWrite: alwaysWrite,
            context: context);
    }
}
