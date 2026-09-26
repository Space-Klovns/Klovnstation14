using System.Text.Json;
using System.Text.Json.Serialization;

namespace Content.Server._KS14.Llm;

// Wire format of the OpenAI-compatible /v1/chat/completions endpoint, as served by llama-server. Only what is
//      sent or read is modelled. Everything the manager keeps between requests is immutable, because a
//      snapshot of it is serialized on a background thread while the main thread carries on.

public static class KsLlmWireRoles
{
    public const string System = "system";
    public const string User = "user";
    public const string Assistant = "assistant";
    public const string Tool = "tool";
}

/// <summary>
///     One message in a conversation.
/// </summary>
public sealed record KsLlmMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("tool_calls")] IReadOnlyList<KsLlmWireToolCall>? ToolCalls = null,
    [property: JsonPropertyName("tool_call_id")] string? ToolCallId = null);

public sealed record KsLlmWireToolCall(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("function")] KsLlmWireFunctionCall Function)
{
    [JsonPropertyName("type")]
    public string Type => "function";
}

public sealed record KsLlmWireFunctionCall(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("arguments")] string Arguments);

public sealed record KsLlmWireTool(
    [property: JsonPropertyName("function")] KsLlmWireFunction Function)
{
    [JsonPropertyName("type")]
    public string Type => "function";
}

public sealed record KsLlmWireFunction(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("parameters")] JsonElement Parameters);

public sealed record KsLlmWireRequest
{
    [JsonPropertyName("messages")]
    public required IReadOnlyList<KsLlmMessage> Messages { get; init; }

    [JsonPropertyName("tools")]
    public IReadOnlyList<KsLlmWireTool>? Tools { get; init; }

    /// <summary>
    ///     <c>"none"</c> forces a text answer; null leaves it to the model.
    /// </summary>
    [JsonPropertyName("tool_choice")]
    public string? ToolChoice { get; init; }

    /// <summary>
    ///     A <c>json_schema</c> response format, which llama-server compiles into a sampling grammar.
    /// </summary>
    [JsonPropertyName("response_format")]
    public JsonElement? ResponseFormat { get; init; }

    [JsonPropertyName("temperature")]
    public float Temperature { get; init; }

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; init; }

    [JsonPropertyName("stream")]
    public bool Stream => false;
}

public sealed class KsLlmWireResponse
{
    [JsonPropertyName("choices")]
    public List<KsLlmWireChoice>? Choices { get; set; }

    [JsonPropertyName("usage")]
    public KsLlmWireUsage? Usage { get; set; }
}

public sealed class KsLlmWireChoice
{
    [JsonPropertyName("message")]
    public KsLlmWireResponseMessage? Message { get; set; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }
}

public sealed class KsLlmWireResponseMessage
{
    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("tool_calls")]
    public List<KsLlmWireResponseToolCall>? ToolCalls { get; set; }
}

public sealed class KsLlmWireResponseToolCall
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("function")]
    public KsLlmWireResponseFunctionCall? Function { get; set; }
}

public sealed class KsLlmWireResponseFunctionCall
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    ///     A JSON-encoded string per the OpenAI format, though some servers send the object itself.
    /// </summary>
    [JsonPropertyName("arguments")]
    public JsonElement Arguments { get; set; }

    public string ArgumentsText => Arguments.ValueKind switch
    {
        JsonValueKind.String => Arguments.GetString() ?? string.Empty,
        JsonValueKind.Undefined or JsonValueKind.Null => string.Empty,
        _ => Arguments.GetRawText(),
    };
}

public sealed class KsLlmWireUsage
{
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; set; }

    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; set; }
}
