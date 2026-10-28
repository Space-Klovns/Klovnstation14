#nullable enable
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Validation;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Serialization.TypeSerializers.Interfaces;

namespace Content.IntegrationTests.Tests._KS14.Serialization;

/// <summary>Prototype roundtrips may preserve invalid defaults, never live network or map references.</summary>
public sealed class KsPrototypeDefaultReferenceSerializer : ITypeSerializer<NetEntity, ValueDataNode>,
    ITypeSerializer<MapId, ValueDataNode>
{
    ValidationNode ITypeValidator<NetEntity, ValueDataNode>.Validate(ISerializationManager serializationManager,
        ValueDataNode node, IDependencyCollection dependencies, ISerializationContext? context) => Validate(node, "invalid");

    ValidationNode ITypeValidator<MapId, ValueDataNode>.Validate(ISerializationManager serializationManager,
        ValueDataNode node, IDependencyCollection dependencies, ISerializationContext? context) => Validate(node, "nullspace");

    private static ValidationNode Validate(ValueDataNode node, string expected) =>
        !node.IsNull && node.Value == expected ? new ValidatedValueNode(node) :
            new ErrorNode(node, "Prototype defaults must not reference a live world.");

    NetEntity ITypeReader<NetEntity, ValueDataNode>.Read(ISerializationManager serializationManager, ValueDataNode node,
        IDependencyCollection dependencies, SerializationHookContext hookCtx, ISerializationContext? context,
        ISerializationManager.InstantiationDelegate<NetEntity>? instanceProvider) =>
        node.Value == "invalid" && !node.IsNull ? NetEntity.Invalid :
            throw new InvalidOperationException("Prototype data contains a network entity reference.");

    MapId ITypeReader<MapId, ValueDataNode>.Read(ISerializationManager serializationManager, ValueDataNode node,
        IDependencyCollection dependencies, SerializationHookContext hookCtx, ISerializationContext? context,
        ISerializationManager.InstantiationDelegate<MapId>? instanceProvider) =>
        node.Value == "nullspace" && !node.IsNull ? MapId.Nullspace :
            throw new InvalidOperationException("Prototype data contains a map reference.");

    public DataNode Write(ISerializationManager serializationManager, NetEntity value, IDependencyCollection dependencies,
        bool alwaysWrite = false, ISerializationContext? context = null) => value == NetEntity.Invalid
        ? new ValueDataNode("invalid") : throw new InvalidOperationException("Prototype data contains a network entity reference.");

    public DataNode Write(ISerializationManager serializationManager, MapId value, IDependencyCollection dependencies,
        bool alwaysWrite = false, ISerializationContext? context = null) => value == MapId.Nullspace
        ? new ValueDataNode("nullspace") : throw new InvalidOperationException("Prototype data contains a map reference.");
}
