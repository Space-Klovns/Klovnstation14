using System.Linq;
using Content.Shared.Chemistry.Reaction;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Validation;
using Robust.Shared.Serialization.TypeSerializers.Interfaces;

namespace Content.Shared._KS14.Chemistry.PredictedTileReactions;

/// <summary>
///     Reads a reagent's <c>tileReactions</c> on both sides. On the server, every entry, as a plain list would. On the
///         client, only the entries whose reaction type the client has, skipping the rest, in their order.
/// </summary>
/// <remarks>
///     Most tile reactions live in <c>Content.Server</c>, so the field used to be server-only and the client had none.
///         A reaction the client predicts (a spill, a foam blanket) is a client-side class of the same name as the
///         server's, which is all a <c>!type:</c> tag names. That lets the client read the one field the server does,
///         with no second list of reactions to keep in step, and no upstream reaction moved out of the server.
///         An unknown type is still an error on the server, where it can only be a typo.
/// </remarks>
public sealed class KsTileReactionListSerializer :
    ITypeSerializer<List<ITileReaction>, SequenceDataNode>,
    ITypeCopyCreator<List<ITileReaction>>
{
    private const string TypeTagPrefix = "!type:";

    /// <summary>
    ///     Whether <paramref name="node"/> names a reaction type that does not exist on this side, on the client.
    /// </summary>
    private static bool IsSkippedHere(ISerializationManager serializationManager, DataNode node)
    {
        if (serializationManager.IsServer ||
            node.Tag is not { } tag ||
            !tag.StartsWith(TypeTagPrefix, StringComparison.Ordinal))
            return false;

        return serializationManager.ReflectionManager.YamlTypeTagLookup(typeof(ITileReaction), tag[TypeTagPrefix.Length..]) == null;
    }

    public List<ITileReaction> Read(
        ISerializationManager serializationManager,
        SequenceDataNode node,
        IDependencyCollection dependencies,
        SerializationHookContext hookCtx,
        ISerializationContext? context = null,
        ISerializationManager.InstantiationDelegate<List<ITileReaction>>? instanceProvider = null)
    {
        var list = instanceProvider != null ? instanceProvider() : new List<ITileReaction>(node.Count);
        foreach (var elementNode in node)
        {
            if (IsSkippedHere(serializationManager, elementNode))
                continue;

            list.Add(serializationManager.Read<ITileReaction>(elementNode, hookCtx, context, notNullableOverride: true));
        }

        return list;
    }

    public ValidationNode Validate(
        ISerializationManager serializationManager,
        SequenceDataNode node,
        IDependencyCollection dependencies,
        ISerializationContext? context = null)
    {
        var validated = new List<ValidationNode>(node.Count);
        foreach (var elementNode in node)
        {
            if (IsSkippedHere(serializationManager, elementNode))
                continue;

            validated.Add(serializationManager.ValidateNode<ITileReaction>(elementNode, context));
        }

        return new ValidatedSequenceNode(validated);
    }

    public DataNode Write(
        ISerializationManager serializationManager,
        List<ITileReaction> value,
        IDependencyCollection dependencies,
        bool alwaysWrite = false,
        ISerializationContext? context = null)
    {
        var sequence = new SequenceDataNode();
        foreach (var reaction in value)
        {
            sequence.Add(serializationManager.WriteValue(reaction, alwaysWrite, context, notNullableOverride: true));
        }

        return sequence;
    }

    public List<ITileReaction> CreateCopy(
        ISerializationManager serializationManager,
        List<ITileReaction> source,
        IDependencyCollection dependencies,
        SerializationHookContext hookCtx,
        ISerializationContext? context = null)
    {
        return source.Select(reaction => serializationManager.CreateCopy(reaction, hookCtx, context, notNullableOverride: true)).ToList();
    }
}
