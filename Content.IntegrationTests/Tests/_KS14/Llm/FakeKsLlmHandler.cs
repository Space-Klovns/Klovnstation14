#nullable enable
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;

namespace Content.IntegrationTests.Tests._KS14.Llm;

/// <summary>
///     Stands in for llama-server behind <c>KsLlmManager.HandlerOverride</c>. Answers <c>/health</c> and
///         <c>/props</c>, records every chat-completion request body, and replies to each with the next scripted
///         response - or a plain text reply once the script runs out. Called from background threads.
/// </summary>
public sealed class FakeKsLlmHandler : HttpMessageHandler
{
    public const int ContextSize = 4096;

    private readonly ConcurrentQueue<Func<Task<string>>> _responses = new();
    private readonly List<string> _requests = [];

    public string DefaultReply = "Acknowledged. Nanotrasen Central Command";

    /// <summary>
    ///     While true, every request fails the way an unreachable host does - as if the PC were switched off.
    /// </summary>
    public volatile bool Unreachable;

    /// <summary>
    ///     When set, everything but <c>/health</c> answers 401 unless sent this key, as llama-server does with
    ///         <c>--api-key</c>.
    /// </summary>
    public volatile string? RequiredApiKey;

    /// <summary>
    ///     The Authorization header of the most recent request, if any.
    /// </summary>
    public volatile string? LastAuthorization;

    private int _attempts;

    /// <summary>
    ///     Every request received, reachable or not.
    /// </summary>
    public int Attempts => Volatile.Read(ref _attempts);

    /// <summary>
    ///     Snapshot of every chat-completion request body, oldest first.
    /// </summary>
    public List<string> Requests
    {
        get
        {
            lock (_requests)
            {
                return [.. _requests];
            }
        }
    }

    public void EnqueueText(string content, int promptTokens = 50, int completionTokens = 10)
    {
        var body = BuildResponse(content, toolCalls: null, promptTokens, completionTokens);
        _responses.Enqueue(() => Task.FromResult(body));
    }

    public void EnqueueToolCall(string name, string argumentsJson, int promptTokens = 50)
    {
        var toolCalls = new JsonArray
        {
            new JsonObject
            {
                ["id"] = $"call_{Guid.NewGuid():N}",
                ["type"] = "function",
                ["function"] = new JsonObject { ["name"] = name, ["arguments"] = argumentsJson },
            },
        };

        var body = BuildResponse(content: null, toolCalls, promptTokens, completionTokens: 10);
        _responses.Enqueue(() => Task.FromResult(body));
    }

    /// <summary>
    ///     The next response is withheld until <paramref name="gate"/> completes.
    /// </summary>
    public void EnqueueGated(Task gate, string content)
    {
        var body = BuildResponse(content, toolCalls: null, promptTokens: 50, completionTokens: 10);
        _responses.Enqueue(async () =>
        {
            await gate;
            return body;
        });
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        Interlocked.Increment(ref _attempts);

        if (Unreachable)
            throw new HttpRequestException("No route to host (fake)");

        LastAuthorization = request.Headers.Authorization?.ToString();
        if (RequiredApiKey is { } requiredApiKey && path != "/health" && LastAuthorization != $"Bearer {requiredApiKey}")
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);

        if (path == "/health")
            return Json("{\"status\":\"ok\"}");

        if (path == "/props")
            return Json($"{{\"default_generation_settings\":{{\"n_ctx\":{ContextSize}}}}}");

        if (path == "/v1/models")
            return Json("{\"object\":\"list\",\"data\":[]}");

        if (path != "/v1/chat/completions")
            return new HttpResponseMessage(HttpStatusCode.NotFound);

        var requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
        lock (_requests)
        {
            _requests.Add(requestBody);
        }

        var responseBody = _responses.TryDequeue(out var response)
            ? await response()
            : BuildResponse(DefaultReply, toolCalls: null, promptTokens: 50, completionTokens: 10);

        return Json(responseBody);
    }

    private static HttpResponseMessage Json(string body)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }

    private static string BuildResponse(string? content, JsonArray? toolCalls, int promptTokens, int completionTokens)
    {
        var message = new JsonObject { ["role"] = "assistant", ["content"] = content };
        if (toolCalls != null)
            message["tool_calls"] = toolCalls;

        var response = new JsonObject
        {
            ["choices"] = new JsonArray
            {
                new JsonObject
                {
                    ["message"] = message,
                    ["finish_reason"] = toolCalls != null ? "tool_calls" : "stop",
                },
            },
            ["usage"] = new JsonObject
            {
                ["prompt_tokens"] = promptTokens,
                ["completion_tokens"] = completionTokens,
            },
        };

        return response.ToJsonString();
    }

    /// <summary>
    ///     The messages array of a recorded request.
    /// </summary>
    public static List<JsonElement> GetMessages(string requestBody)
    {
        using var document = JsonDocument.Parse(requestBody);
        var messages = new List<JsonElement>();
        foreach (var message in document.RootElement.GetProperty("messages").EnumerateArray())
            messages.Add(message.Clone());

        return messages;
    }
}
