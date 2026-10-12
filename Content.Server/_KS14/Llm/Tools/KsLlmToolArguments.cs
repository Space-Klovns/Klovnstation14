using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using Content.Server._KS14.Llm.Prototypes;

namespace Content.Server._KS14.Llm.Tools;

/// <summary>
///     The arguments of a tool call, checked against the tool's <see cref="KsLlmToolParameter"/>s. Nothing
///         reaches a <see cref="KsLlmToolEffect"/> without passing through <see cref="TryParse"/>.
/// </summary>
public sealed class KsLlmToolArguments
{
    private readonly Dictionary<string, object> _values;

    private KsLlmToolArguments(Dictionary<string, object> values)
    {
        _values = values;
    }

    public string? GetString(string name) => _values.TryGetValue(name, out var value) ? value as string : null;

    public bool? GetBool(string name) => _values.TryGetValue(name, out var value) && value is bool boolValue ? boolValue : null;

    public int? GetInt(string name) => _values.TryGetValue(name, out var value) && value is int intValue ? intValue : null;

    /// <summary>
    ///     Parses and validates <paramref name="argumentsJson"/>. On failure, <paramref name="error"/> is a
    ///         model-facing explanation it can correct from.
    /// </summary>
    public static bool TryParse(KsLlmToolPrototype tool,
        string argumentsJson,
        [NotNullWhen(true)] out KsLlmToolArguments? arguments,
        [NotNullWhen(false)] out string? error)
    {
        arguments = null;

        // Some models send nothing at all for a tool without parameters.
        if (string.IsNullOrWhiteSpace(argumentsJson))
            argumentsJson = "{}";

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(argumentsJson);
        }
        catch (JsonException)
        {
            error = $"The arguments for '{tool.Name}' were not valid JSON.";
            return false;
        }

        using (document)
            return TryParse(tool, document.RootElement, out arguments, out error);
    }

    public static bool TryParse(KsLlmToolPrototype tool,
        JsonElement argumentsElement,
        [NotNullWhen(true)] out KsLlmToolArguments? arguments,
        [NotNullWhen(false)] out string? error)
    {
        arguments = null;

        if (argumentsElement.ValueKind != JsonValueKind.Object)
        {
            error = $"The arguments for '{tool.Name}' must be a JSON object.";
            return false;
        }

        var values = new Dictionary<string, object>();
        foreach (var parameter in tool.Parameters)
        {
            if (!argumentsElement.TryGetProperty(parameter.Name, out var valueElement)
                || valueElement.ValueKind == JsonValueKind.Null)
            {
                if (!parameter.Required)
                    continue;

                error = $"Missing required argument '{parameter.Name}'.";
                return false;
            }

            if (!TryReadValue(parameter, valueElement, out var value, out error))
                return false;

            values[parameter.Name] = value;
        }

        arguments = new KsLlmToolArguments(values);
        error = null;
        return true;
    }

    private static bool TryReadValue(KsLlmToolParameter parameter,
        JsonElement valueElement,
        [NotNullWhen(true)] out object? value,
        [NotNullWhen(false)] out string? error)
    {
        value = null;
        error = null;

        switch (parameter.Type)
        {
            case KsLlmToolParameterType.String:
            {
                if (valueElement.ValueKind != JsonValueKind.String)
                {
                    error = $"Argument '{parameter.Name}' must be a string.";
                    return false;
                }

                var stringValue = valueElement.GetString()!.Trim();
                if (parameter.MaxLength is { } maxLength && stringValue.Length > maxLength)
                {
                    error = $"Argument '{parameter.Name}' is longer than {maxLength} characters.";
                    return false;
                }

                if (parameter.Enum.Count > 0)
                {
                    var match = parameter.Enum.Find(option => string.Equals(option, stringValue, StringComparison.OrdinalIgnoreCase));
                    if (match == null)
                    {
                        error = $"Argument '{parameter.Name}' must be one of: {string.Join(", ", parameter.Enum)}.";
                        return false;
                    }

                    stringValue = match;
                }

                value = stringValue;
                return true;
            }
            case KsLlmToolParameterType.Integer:
            {
                int intValue;
                // Small models quote numbers now and then; the intent is unambiguous, so accept it.
                if (valueElement.ValueKind == JsonValueKind.Number && valueElement.TryGetInt32(out var parsed))
                    intValue = parsed;
                else if (valueElement.ValueKind == JsonValueKind.String
                         && int.TryParse(valueElement.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                    intValue = parsed;
                else
                {
                    error = $"Argument '{parameter.Name}' must be an integer.";
                    return false;
                }

                if (parameter.Min is { } min && intValue < min || parameter.Max is { } max && intValue > max)
                {
                    error = $"Argument '{parameter.Name}' must be between {parameter.Min?.ToString() ?? "-inf"} and {parameter.Max?.ToString() ?? "inf"}.";
                    return false;
                }

                value = intValue;
                return true;
            }
            case KsLlmToolParameterType.Boolean:
            {
                switch (valueElement.ValueKind)
                {
                    case JsonValueKind.True:
                        value = true;
                        return true;
                    case JsonValueKind.False:
                        value = false;
                        return true;
                    case JsonValueKind.String when bool.TryParse(valueElement.GetString(), out var boolValue):
                        value = boolValue;
                        return true;
                    default:
                        error = $"Argument '{parameter.Name}' must be true or false.";
                        return false;
                }
            }
            default:
                error = $"Argument '{parameter.Name}' has an unsupported type.";
                return false;
        }
    }
}
