using System.Linq;

namespace Content.Server._KS14.Llm.Prototypes;

public enum KsLlmToolParameterType : byte
{
    String,
    Integer,
    Boolean,
}

/// <summary>
///     One argument of a <see cref="KsLlmToolPrototype"/>. Rendered into the JSON schema the model sees, and
///         checked against every call it makes.
/// </summary>
[DataDefinition]
public sealed partial class KsLlmToolParameter
{
    /// <summary>
    ///     The argument's key in the call, e.g. <c>target_name</c>.
    /// </summary>
    [DataField(required: true)]
    public string Name = string.Empty;

    [DataField(required: true)]
    public KsLlmToolParameterType Type = KsLlmToolParameterType.String;

    /// <summary>
    ///     Model-facing.
    /// </summary>
    [DataField]
    public string Description = string.Empty;

    [DataField]
    public bool Required = true;

    /// <summary>
    ///     For strings: the longest value accepted.
    /// </summary>
    [DataField]
    public int? MaxLength;

    /// <summary>
    ///     For strings: when non-empty, the only values accepted (compared case-insensitively).
    /// </summary>
    [DataField]
    public List<string> Enum = new();

    /// <summary>
    ///     For strings: what each <see cref="Enum"/> value means, keyed by value. Model-facing; appended to
    ///         <see cref="Description"/> wherever the model is shown this parameter.
    /// </summary>
    [DataField]
    public Dictionary<string, string> EnumDescriptions = new();

    /// <summary>
    ///     For integers: inclusive bounds.
    /// </summary>
    [DataField]
    public int? Min;

    [DataField]
    public int? Max;

    /// <summary>
    ///     <see cref="Description"/> followed by the meaning of each value that has one - everything the model
    ///         should be told about this parameter.
    /// </summary>
    public string GetFullDescription()
    {
        if (EnumDescriptions.Count == 0)
            return Description;

        var values = Enum
            .Where(EnumDescriptions.ContainsKey)
            .Select(value => $"'{value}': {EnumDescriptions[value]}");

        return string.IsNullOrEmpty(Description)
            ? string.Join(" ", values)
            : $"{Description} {string.Join(" ", values)}";
    }
}
