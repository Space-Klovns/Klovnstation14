using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Validation;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Serialization.TypeSerializers.Interfaces;

namespace Content.Shared._KS14.Fluidmos.Serialization;

/// <summary>
///     Reads and writes <see cref="KsFluidMixture.Reagents"/> as a sequence of entries, so a mapper can prefill a canister:
///     <code>
///     fluid:
///       reagents:
///       - reagent: Water
///         liquid: 100
///     </code>
///     Each entry takes <c>reagent</c>, and optionally <c>solid</c>, <c>liquid</c>, <c>vapor</c> (moles) and <c>data</c>.
/// </summary>
public sealed class KsFluidReagentsSerializer :
    ITypeSerializer<KsFluidReagentTable, SequenceDataNode>,
    ITypeCopyCreator<KsFluidReagentTable>
{
    private const string ReagentKey = "reagent";
    private const string SolidKey = "solid";
    private const string LiquidKey = "liquid";
    private const string VaporKey = "vapor";
    private const string DataKey = "data";

    public ValidationNode Validate(
        ISerializationManager serializationManager,
        SequenceDataNode node,
        IDependencyCollection dependencies,
        ISerializationContext? context = null)
    {
        var list = new List<ValidationNode>();

        foreach (var element in node.Sequence)
        {
            if (element is not MappingDataNode mapping)
            {
                list.Add(new ErrorNode(element, "Expected a mapping for a fluid reagent entry."));
                continue;
            }

            if (!mapping.TryGet(ReagentKey, out var reagentNode))
            {
                list.Add(new ErrorNode(mapping, $"Fluid reagent entry has no '{ReagentKey}'."));
                continue;
            }

            list.Add(serializationManager.ValidateNode<ProtoId<ReagentPrototype>>(reagentNode, context));

            foreach (var amountKey in new[] { SolidKey, LiquidKey, VaporKey })
            {
                if (mapping.TryGet(amountKey, out var amountNode))
                    list.Add(serializationManager.ValidateNode<float>(amountNode, context));
            }

            if (mapping.TryGet(DataKey, out var dataNode))
                list.Add(serializationManager.ValidateNode<List<ReagentData>>(dataNode, context));
        }

        return new ValidatedSequenceNode(list);
    }

    public KsFluidReagentTable Read(
        ISerializationManager serializationManager,
        SequenceDataNode node,
        IDependencyCollection dependencies,
        SerializationHookContext hookCtx,
        ISerializationContext? context = null,
        ISerializationManager.InstantiationDelegate<KsFluidReagentTable>? instanceProvider = null)
    {
        var reagents = instanceProvider != null ? instanceProvider() : new KsFluidReagentTable(node.Sequence.Count);

        foreach (var element in node.Sequence)
        {
            if (element is not MappingDataNode mapping || !mapping.TryGet<ValueDataNode>(ReagentKey, out var reagentNode))
                continue;

            List<ReagentData>? data = null;
            if (mapping.TryGet(DataKey, out var dataNode))
                data = serializationManager.Read<List<ReagentData>>(dataNode, hookCtx, context, notNullableOverride: true);

            var key = new KsFluidReagentKey(reagentNode.Value, KsReagentDataSet.From(data));
            reagents.TryGetAmount(key, out var amount);
            amount.Solid += ReadAmount(serializationManager, mapping, SolidKey, hookCtx, context);
            amount.Liquid += ReadAmount(serializationManager, mapping, LiquidKey, hookCtx, context);
            amount.Vapor += ReadAmount(serializationManager, mapping, VaporKey, hookCtx, context);

            reagents.Set(key, amount);
        }

        return reagents;
    }

    private static float ReadAmount(
        ISerializationManager serializationManager,
        MappingDataNode mapping,
        string key,
        SerializationHookContext hookCtx,
        ISerializationContext? context)
    {
        if (!mapping.TryGet(key, out var node))
            return 0f;

        return MathF.Max(0f, serializationManager.Read<float>(node, hookCtx, context));
    }

    public DataNode Write(
        ISerializationManager serializationManager,
        KsFluidReagentTable value,
        IDependencyCollection dependencies,
        bool alwaysWrite = false,
        ISerializationContext? context = null)
    {
        var sequence = new SequenceDataNode();

        foreach (var (key, amount) in value)
        {
            var mapping = new MappingDataNode();
            mapping.Add(ReagentKey, new ValueDataNode(key.Prototype.Id));

            if (amount.Solid > 0f)
                mapping.Add(SolidKey, serializationManager.WriteValue(amount.Solid, alwaysWrite, context));

            if (amount.Liquid > 0f)
                mapping.Add(LiquidKey, serializationManager.WriteValue(amount.Liquid, alwaysWrite, context));

            if (amount.Vapor > 0f)
                mapping.Add(VaporKey, serializationManager.WriteValue(amount.Vapor, alwaysWrite, context));

            if (key.Data != null)
                mapping.Add(DataKey, serializationManager.WriteValue(key.Data.ToList(), alwaysWrite, context, notNullableOverride: true));

            sequence.Add(mapping);
        }

        return sequence;
    }

    public KsFluidReagentTable CreateCopy(
        ISerializationManager serializationManager,
        KsFluidReagentTable source,
        IDependencyCollection dependencies,
        SerializationHookContext hookCtx,
        ISerializationContext? context = null)
    {
        // Keys are immutable (their data sets included), so copying the arrays is a full copy.
        return source.Clone();
    }
}
