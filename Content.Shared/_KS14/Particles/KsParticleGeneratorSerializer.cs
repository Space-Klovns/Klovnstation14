using System.Globalization;
using System.Numerics;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Validation;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Serialization.TypeSerializers.Interfaces;

namespace Content.Shared._KS14.Particles;

/// <summary>
///     Reads a <see cref="KsParticleGenerator"/> from any of three shapes, so a BYOND particle definition copies
///         across line by line:
///     <list type="bullet">
///         <item><c>5</c>, <c>"#ff8800"</c>, <c>red</c>: a constant scalar or colour.</item>
///         <item><c>[ 0, 0.5 ]</c>, <c>[ 0, 0.5, 1 ]</c>: a constant vector; a missing z is 0.</item>
///         <item><c>{ kind: circle, a: 0, b: 0.5, dist: normal }</c>: a generator, whose operands take the first two
///             shapes.</item>
///     </list>
/// </summary>
[TypeSerializer]
public sealed class KsParticleGeneratorSerializer :
    ITypeSerializer<KsParticleGenerator, ValueDataNode>,
    ITypeSerializer<KsParticleGenerator, SequenceDataNode>,
    ITypeSerializer<KsParticleGenerator, MappingDataNode>,
    ITypeCopyCreator<KsParticleGenerator>
{
    private const string KindKey = "kind";
    private const string AKey = "a";
    private const string BKey = "b";
    private const string DistributionKey = "dist";

    /// <summary>BYOND's <c>"color"</c> generator type: a <see cref="KsParticleGeneratorKind.Vector"/> over colours.</summary>
    private const string ColorKind = "color";

    #region Operands

    /// <summary>
    ///     Reads one operand: a scalar, a colour, or a list of two to four numbers.
    /// </summary>
    private static bool TryReadOperand(DataNode node, out Vector4 value, out bool isColor, out byte components, out string? error)
    {
        value = Vector4.Zero;
        isColor = false;
        components = 1;
        error = null;

        switch (node)
        {
            case ValueDataNode valueNode:
                if (float.TryParse(valueNode.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var scalar))
                {
                    value = new Vector4(scalar);
                    return true;
                }

                if (Color.TryFromHex(valueNode.Value, out var hexColor))
                {
                    value = KsParticleGenerator.ToVector(hexColor);
                    isColor = true;
                    components = 4;
                    return true;
                }

                if (Color.TryFromName(valueNode.Value, out var namedColor))
                {
                    value = KsParticleGenerator.ToVector(namedColor);
                    isColor = true;
                    components = 4;
                    return true;
                }

                error = $"'{valueNode.Value}' is neither a number nor a colour.";
                return false;

            case SequenceDataNode sequenceNode:
            {
                if (sequenceNode.Count is < 2 or > 4)
                {
                    error = $"A vector needs 2 to 4 numbers, not {sequenceNode.Count}.";
                    return false;
                }

                var parsed = new float[4];
                for (var i = 0; i < sequenceNode.Count; i++)
                {
                    if (sequenceNode[i] is not ValueDataNode elementNode ||
                        !float.TryParse(elementNode.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed[i]))
                    {
                        error = $"Vector component {i} is not a number.";
                        return false;
                    }
                }

                value = new Vector4(parsed[0], parsed[1], parsed[2], parsed[3]);
                components = (byte)sequenceNode.Count;
                return true;
            }

            default:
                error = "An operand is a number, a colour or a list of numbers.";
                return false;
        }
    }

    private static DataNode WriteOperand(Vector4 value, bool isColor, byte components)
    {
        if (isColor)
            return new ValueDataNode(KsParticleGenerator.ToColor(value).ToHex());

        if (components <= 1)
            return new ValueDataNode(value.X.ToString(CultureInfo.InvariantCulture));

        var sequence = new SequenceDataNode();
        sequence.Add(new ValueDataNode(value.X.ToString(CultureInfo.InvariantCulture)));
        sequence.Add(new ValueDataNode(value.Y.ToString(CultureInfo.InvariantCulture)));
        if (components >= 3)
            sequence.Add(new ValueDataNode(value.Z.ToString(CultureInfo.InvariantCulture)));
        if (components >= 4)
            sequence.Add(new ValueDataNode(value.W.ToString(CultureInfo.InvariantCulture)));
        return sequence;
    }

    private static bool TryParseKind(string text, out KsParticleGeneratorKind kind, out bool isColor)
    {
        isColor = false;
        if (string.Equals(text, ColorKind, StringComparison.OrdinalIgnoreCase))
        {
            kind = KsParticleGeneratorKind.Vector;
            isColor = true;
            return true;
        }

        return Enum.TryParse(text, ignoreCase: true, out kind);
    }

    #endregion

    #region Mapping

    private static bool TryReadMapping(MappingDataNode node, out KsParticleGenerator generator, out string? error)
    {
        generator = default;
        error = null;

        if (!node.TryGet(KindKey, out var kindNode) || kindNode is not ValueDataNode kindValueNode)
        {
            error = $"A generator mapping needs a '{KindKey}'.";
            return false;
        }

        if (!TryParseKind(kindValueNode.Value, out var kind, out var kindIsColor))
        {
            error = $"'{kindValueNode.Value}' is not a generator kind.";
            return false;
        }

        var a = Vector4.Zero;
        var b = Vector4.Zero;
        var isColor = kindIsColor;
        byte components = 1;

        if (node.TryGet(AKey, out var aNode))
        {
            if (!TryReadOperand(aNode, out a, out var aIsColor, out components, out error))
                return false;
            isColor |= aIsColor;
        }

        if (node.TryGet(BKey, out var bNode))
        {
            if (!TryReadOperand(bNode, out b, out var bIsColor, out var bComponents, out error))
                return false;
            isColor |= bIsColor;
            components = Math.Max(components, bComponents);
        }
        else if (kind != KsParticleGeneratorKind.Constant)
        {
            b = a;
        }

        var distribution = KsParticleDistribution.Uniform;
        if (node.TryGet(DistributionKey, out var distributionNode))
        {
            if (distributionNode is not ValueDataNode distributionValueNode ||
                !Enum.TryParse(distributionValueNode.Value, ignoreCase: true, out distribution))
            {
                error = $"'{DistributionKey}' is one of uniform, normal, linear or square.";
                return false;
            }
        }

        foreach (var (keyNode, _) in node)
        {
            if (keyNode is not KindKey and not AKey and not BKey and not DistributionKey)
            {
                error = $"'{keyNode}' is not a generator field.";
                return false;
            }
        }

        generator = new KsParticleGenerator(kind, a, b, distribution, isColor, components);
        return true;
    }

    #endregion

    #region Read

    public KsParticleGenerator Read(ISerializationManager serializationManager, ValueDataNode node, IDependencyCollection dependencies, SerializationHookContext hookCtx, ISerializationContext? context = null, ISerializationManager.InstantiationDelegate<KsParticleGenerator>? instanceProvider = null)
    {
        if (!TryReadOperand(node, out var value, out var isColor, out var components, out var error))
            throw new InvalidMappingException($"Could not read particle generator: {error}");

        return new KsParticleGenerator(KsParticleGeneratorKind.Constant, value, Vector4.Zero, isColor: isColor, components: components);
    }

    public KsParticleGenerator Read(ISerializationManager serializationManager, SequenceDataNode node, IDependencyCollection dependencies, SerializationHookContext hookCtx, ISerializationContext? context = null, ISerializationManager.InstantiationDelegate<KsParticleGenerator>? instanceProvider = null)
    {
        if (!TryReadOperand(node, out var value, out var isColor, out var components, out var error))
            throw new InvalidMappingException($"Could not read particle generator: {error}");

        return new KsParticleGenerator(KsParticleGeneratorKind.Constant, value, Vector4.Zero, isColor: isColor, components: components);
    }

    public KsParticleGenerator Read(ISerializationManager serializationManager, MappingDataNode node, IDependencyCollection dependencies, SerializationHookContext hookCtx, ISerializationContext? context = null, ISerializationManager.InstantiationDelegate<KsParticleGenerator>? instanceProvider = null)
    {
        if (!TryReadMapping(node, out var generator, out var error))
            throw new InvalidMappingException($"Could not read particle generator: {error}");

        return generator;
    }

    #endregion

    #region Validate

    public ValidationNode Validate(ISerializationManager serializationManager, ValueDataNode node, IDependencyCollection dependencies, ISerializationContext? context = null)
    {
        return TryReadOperand(node, out _, out _, out _, out var error)
            ? new ValidatedValueNode(node)
            : new ErrorNode(node, error!);
    }

    public ValidationNode Validate(ISerializationManager serializationManager, SequenceDataNode node, IDependencyCollection dependencies, ISerializationContext? context = null)
    {
        if (!TryReadOperand(node, out _, out _, out _, out var error))
            return new ErrorNode(node, error!);

        var validated = new List<ValidationNode>(node.Count);
        foreach (var elementNode in node)
            validated.Add(new ValidatedValueNode((ValueDataNode)elementNode));
        return new ValidatedSequenceNode(validated);
    }

    public ValidationNode Validate(ISerializationManager serializationManager, MappingDataNode node, IDependencyCollection dependencies, ISerializationContext? context = null)
    {
        if (!TryReadMapping(node, out _, out var error))
            return new ErrorNode(node, error!);

        var validated = new Dictionary<ValidationNode, ValidationNode>();
        foreach (var (keyNode, valueNode) in node)
        {
            validated.Add(new ValidatedValueNode(new ValueDataNode(keyNode)), valueNode switch
            {
                SequenceDataNode sequenceNode => Validate(serializationManager, sequenceNode, dependencies, context),
                _ => new ValidatedValueNode((ValueDataNode)valueNode),
            });
        }

        return new ValidatedMappingNode(validated);
    }

    #endregion

    public DataNode Write(ISerializationManager serializationManager, KsParticleGenerator value, IDependencyCollection dependencies, bool alwaysWrite = false, ISerializationContext? context = null)
    {
        if (value.Kind == KsParticleGeneratorKind.Constant)
            return WriteOperand(value.A, value.IsColor, value.Components);

        var mapping = new MappingDataNode();
        mapping.Add(KindKey, new ValueDataNode(value.IsColor && value.Kind == KsParticleGeneratorKind.Vector
            ? ColorKind
            : value.Kind.ToString().ToLowerInvariant()));
        mapping.Add(AKey, WriteOperand(value.A, value.IsColor, value.Components));
        mapping.Add(BKey, WriteOperand(value.B, value.IsColor, value.Components));
        if (value.Distribution != KsParticleDistribution.Uniform)
            mapping.Add(DistributionKey, new ValueDataNode(value.Distribution.ToString().ToLowerInvariant()));
        return mapping;
    }

    public KsParticleGenerator CreateCopy(ISerializationManager serializationManager, KsParticleGenerator source, IDependencyCollection dependencies, SerializationHookContext hookCtx, ISerializationContext? context = null)
    {
        return source;
    }
}
