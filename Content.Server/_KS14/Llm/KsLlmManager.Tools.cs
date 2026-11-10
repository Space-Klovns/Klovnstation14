using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Content.Server._KS14.Llm.Prototypes;

namespace Content.Server._KS14.Llm;

public sealed partial class KsLlmManager
{
    /// <summary>
    ///     A persona's tools in every form a request needs them. Built from prototypes once, then shared by
    ///         reference with background serialization - hence <see cref="JsonElement"/>, which is immutable,
    ///         rather than a mutable <see cref="JsonNode"/>.
    /// </summary>
    private sealed class PersonaTools
    {
        public required Dictionary<string, KsLlmToolPrototype> ToolsByName;
        public required IReadOnlyList<KsLlmWireTool> WireTools;
        public required JsonElement ConstrainedFormat;
        public required JsonElement ConstrainedFinalFormat;
        public required string ConstrainedInstructions;
        public required string ConstrainedFinalInstructions;
    }

    private PersonaTools GetPersonaTools(KsLlmPersonaPrototype persona)
    {
        if (_personaToolsCache.TryGetValue(persona.ID, out var cached))
            return cached;

        var toolsByName = new Dictionary<string, KsLlmToolPrototype>();
        foreach (var toolId in persona.Tools)
        {
            if (!_prototypeManager.TryIndex(toolId, out var tool))
            {
                _sawmill.Error($"Persona {persona.ID} lists unknown tool {toolId}.");
                continue;
            }

            if (!toolsByName.TryAdd(tool.Name, tool))
                _sawmill.Error($"Persona {persona.ID} has two tools named '{tool.Name}'.");
        }

        var wireTools = toolsByName.Values
            .Select(tool => new KsLlmWireTool(new KsLlmWireFunction(tool.Name, DescribeTool(tool), ToElement(BuildParametersSchema(tool)))))
            .ToList();

        var personaTools = new PersonaTools
        {
            ToolsByName = toolsByName,
            WireTools = wireTools,
            ConstrainedFormat = ToElement(BuildConstrainedFormat(toolsByName.Values, allowTools: true)),
            ConstrainedFinalFormat = ToElement(BuildConstrainedFormat(toolsByName.Values, allowTools: false)),
            ConstrainedInstructions = BuildConstrainedInstructions(toolsByName.Values, allowTools: true),
            ConstrainedFinalInstructions = BuildConstrainedInstructions(toolsByName.Values, allowTools: false),
        };

        _personaToolsCache[persona.ID] = personaTools;
        return personaTools;
    }

    /// <summary>
    ///     The tool's description as the model sees it: its own text, then the limits the code enforces on it,
    ///         generated from the prototype so that they cannot disagree with what is actually enforced.
    /// </summary>
    private string DescribeTool(KsLlmToolPrototype tool)
    {
        var builder = new StringBuilder(tool.Description);

        if (tool.RequiredStamps.Count > 0)
        {
            var stampNames = tool.RequiredStamps.Select(stamp => _localizationManager.TryGetString(stamp, out var name) ? name : stamp);
            builder.Append($" Only works on a fax bearing one of these stamps: {string.Join(", ", stampNames)}.");
        }

        if (tool.MaxUsesPerRound is { } maxUses)
            builder.Append(maxUses == 1 ? " Can be used once per shift." : $" Can be used {maxUses} times per shift.");

        return builder.ToString();
    }

    private static JsonElement ToElement(JsonNode node)
    {
        return JsonSerializer.SerializeToElement(node);
    }

    /// <summary>
    ///     The JSON schema of a tool's arguments, straight from its YAML parameters.
    /// </summary>
    private static JsonObject BuildParametersSchema(KsLlmToolPrototype tool)
    {
        var properties = new JsonObject();
        var required = new JsonArray();

        foreach (var parameter in tool.Parameters)
        {
            var property = new JsonObject
            {
                ["type"] = parameter.Type switch
                {
                    KsLlmToolParameterType.Integer => "integer",
                    KsLlmToolParameterType.Boolean => "boolean",
                    _ => "string",
                },
            };

            var description = parameter.GetFullDescription();
            if (!string.IsNullOrEmpty(description))
                property["description"] = description;

            if (parameter.MaxLength is { } maxLength)
                property["maxLength"] = maxLength;

            if (parameter.Enum.Count > 0)
                property["enum"] = new JsonArray(parameter.Enum.Select(option => (JsonNode)JsonValue.Create(option)).ToArray());

            if (parameter.Min is { } min)
                property["minimum"] = min;

            if (parameter.Max is { } max)
                property["maximum"] = max;

            properties[parameter.Name] = property;

            if (parameter.Required)
                required.Add(parameter.Name);
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required,
            ["additionalProperties"] = false,
        };
    }

    /// <summary>
    ///     A <c>json_schema</c> response format admitting exactly <c>{"reply": ...}</c> or, when tools are
    ///         allowed, <c>{"tool": name, "args": {...}}</c> for each tool. llama-server turns it into a grammar,
    ///         so nothing else can be sampled.
    /// </summary>
    private static JsonObject BuildConstrainedFormat(IEnumerable<KsLlmToolPrototype> tools, bool allowTools)
    {
        var options = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject { ["reply"] = new JsonObject { ["type"] = "string" } },
                ["required"] = new JsonArray("reply"),
                ["additionalProperties"] = false,
            },
        };

        if (allowTools)
        {
            foreach (var tool in tools)
            {
                options.Add(new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["tool"] = new JsonObject { ["const"] = tool.Name },
                        ["args"] = BuildParametersSchema(tool),
                    },
                    ["required"] = new JsonArray("tool", "args"),
                    ["additionalProperties"] = false,
                });
            }
        }

        return new JsonObject
        {
            ["type"] = "json_schema",
            ["json_schema"] = new JsonObject
            {
                ["name"] = "response",
                ["schema"] = new JsonObject { ["oneOf"] = options },
            },
        };
    }

    private string BuildConstrainedInstructions(IReadOnlyCollection<KsLlmToolPrototype> tools, bool allowTools)
    {
        var builder = new StringBuilder();
        builder.Append("Respond with exactly one JSON object and nothing else. To reply: {\"reply\": \"<your reply>\"}.");

        if (!allowTools || tools.Count == 0)
        {
            if (tools.Count > 0)
                builder.Append(" You may not use any more tools for this message; reply now.");

            return builder.ToString();
        }

        builder.Append(" To use a tool: {\"tool\": \"<tool name>\", \"args\": {<arguments>}}.");
        builder.Append(" Tool results arrive in messages starting with [tool result]. Available tools:");
        foreach (var tool in tools)
        {
            builder.Append($"\n- {tool.Name}: {DescribeTool(tool)}");
            foreach (var parameter in tool.Parameters)
            {
                var requirement = parameter.Required ? "required" : "optional";
                builder.Append($"\n    - {parameter.Name} ({parameter.Type.ToString().ToLowerInvariant()}, {requirement}): {parameter.GetFullDescription()}");
            }
        }

        return builder.ToString();
    }
}
