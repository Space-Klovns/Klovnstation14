using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._KS14.Llm.Prototypes;
using Content.Server._KS14.Llm.Tools;
using Content.Shared._KS14.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Llm;

/// <summary>
///     Owns the local LLM backend and every conversation with it. There is one of these, backing one model.
/// </summary>
/// <remarks>
///     <para>
///         Threading: every public member, and <see cref="Update"/>, is main-thread only. Anything slow - process
///             launch, health checks, HTTP, JSON - runs in <see cref="Task.Run(Func{Task})"/> against an immutable
///             snapshot, and reports back only by posting a <see cref="BackendEvent"/> into <see cref="_events"/>.
///             <see cref="Update"/> drains those once per tick. So no background task touches manager state or the
///             simulation, and nothing here ever waits on the model.
///     </para>
///     <para>
///         A turn goes: message in → completion request → (tool calls → execute on the main thread next tick →
///             follow-up request)* → text reply → <see cref="KsLlmTurnRequest.OnComplete"/>. One turn is in flight
///             at a time, because the backend serves one slot; the rest wait in <see cref="_queuedTurns"/>.
///     </para>
/// </remarks>
public sealed partial class KsLlmManager
{
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IResourceManager _resourceManager = default!;
    [Dependency] private ILogManager _logManager = default!;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // Reasoning models may leave their thinking in the content when the server isn't set to strip it.
    private static readonly Regex ThinkBlockRegex = new(@"<think>.*?</think>", RegexOptions.Singleline | RegexOptions.Compiled);

    private ISawmill _sawmill = default!;

    /// <summary>
    ///     The only channel from background tasks back to the main thread.
    /// </summary>
    private readonly ConcurrentQueue<BackendEvent> _events = new();

    private readonly Queue<KsLlmTurnRequest> _queuedTurns = new();
    private ActiveTurn? _activeTurn;

    private readonly Dictionary<string, KsLlmConversation> _conversations = new();

    // Built from prototypes on first use, dropped when prototypes reload.
    private readonly Dictionary<string, PersonaTools> _personaToolsCache = new();

    /// <summary>
    ///     Cancelled on <see cref="Shutdown"/>; every background task observes it.
    /// </summary>
    private readonly CancellationTokenSource _lifetimeCancellationTokenSource = new();

    private HttpClient _httpClient = CreateHttpClient(handler: null);

    private int _lastRequestId;
    private int _lastToolCallId;
    private bool _shuttingDown;

    // CVar mirrors, main-thread only.
    private bool _enabled;
    private float _requestTimeout;
    private int _maxToolTurns;
    private int _maxQueuedTurns;
    private bool _autoCompact;
    private float _compactThreshold;
    private int _compactKeepMessages;
    private bool _constrainedTools;

    public KsLlmState State { get; private set; } = KsLlmState.Disabled;

    public int QueuedTurnCount => _queuedTurns.Count;

    public bool TurnInFlight => _activeTurn != null;

    /// <summary>
    ///     Test hook: replaces the HTTP transport, so a test can stand in for the backend. Pair it with
    ///         <see cref="KsCCVars.LlmEndpoint"/> so that no process is spawned.
    /// </summary>
    public HttpMessageHandler? HandlerOverride
    {
        set => _httpClient = CreateHttpClient(value);
    }

    private static HttpClient CreateHttpClient(HttpMessageHandler? handler)
    {
        // A short connect timeout: a remote PC that is switched off usually drops packets rather than refusing
        // them, and without this every request would sit out the whole request timeout before failing.
        handler ??= new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(10) };
        return new HttpClient(handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public void Initialize()
    {
        _sawmill = _logManager.GetSawmill("llm");
        _serverSawmill = _logManager.GetSawmill("llm.server");

        _configurationManager.OnValueChanged(KsCCVars.LlmRequestTimeout, value => _requestTimeout = value, invokeImmediately: true);
        _configurationManager.OnValueChanged(KsCCVars.LlmMaxToolTurns, value => _maxToolTurns = value, invokeImmediately: true);
        _configurationManager.OnValueChanged(KsCCVars.LlmMaxQueuedTurns, value => _maxQueuedTurns = value, invokeImmediately: true);
        _configurationManager.OnValueChanged(KsCCVars.LlmAutoCompact, value => _autoCompact = value, invokeImmediately: true);
        _configurationManager.OnValueChanged(KsCCVars.LlmCompactThreshold, value => _compactThreshold = value, invokeImmediately: true);
        _configurationManager.OnValueChanged(KsCCVars.LlmCompactKeepMessages, value => _compactKeepMessages = value, invokeImmediately: true);
        _configurationManager.OnValueChanged(KsCCVars.LlmConstrainedTools, value => _constrainedTools = value, invokeImmediately: true);
        _configurationManager.OnValueChanged(KsCCVars.LlmRestartAttempts, value => _maxRestartAttempts = value, invokeImmediately: true);
        _configurationManager.OnValueChanged(KsCCVars.LlmEnabled, OnEnabledChanged, invokeImmediately: true);

        _prototypeManager.PrototypesReloaded += OnPrototypesReloaded;

        RegisterExitHandlers();
    }

    public void Update()
    {
        while (_events.TryDequeue(out var backendEvent))
            HandleEvent(backendEvent);

        if (State == KsLlmState.Restarting && _gameTiming.RealTime >= _restartAt)
            StartBackend();

        if (_activeTurn == null && State == KsLlmState.Ready && _queuedTurns.TryDequeue(out var request))
            BeginTurn(request);
    }

    /// <summary>
    ///     Queues a turn. Returns immediately. When this returns false the turn was not accepted and
    ///         <see cref="KsLlmTurnRequest.OnComplete"/> will never be called; when it returns true it will be
    ///         called exactly once.
    /// </summary>
    public bool TryRequestTurn(KsLlmTurnRequest request)
    {
        if (State is not (KsLlmState.Ready or KsLlmState.Busy))
            return false;

        if (_queuedTurns.Count >= _maxQueuedTurns)
        {
            _sawmill.Warning($"Dropped a turn for {request.Persona}: {_queuedTurns.Count} already queued.");
            return false;
        }

        if (!_prototypeManager.HasIndex(request.Persona))
        {
            _sawmill.Error($"Dropped a turn for unknown persona {request.Persona}.");
            return false;
        }

        _queuedTurns.Enqueue(request);
        return true;
    }

    /// <summary>
    ///     Forgets every conversation. A turn in flight finishes against the history it started with, which is
    ///         then discarded.
    /// </summary>
    public void ResetConversation()
    {
        _conversations.Clear();
    }

    /// <summary>
    ///     Ends the turn in flight and every queued one, each with a failed <see cref="KsLlmTurnRequest.OnComplete"/>.
    ///         A late answer to a cancelled request is ignored, so none of its tool calls run.
    /// </summary>
    public void CancelAllTurns()
    {
        FailAllTurns();
    }

    public KsLlmConversation? GetConversation(ProtoId<KsLlmPersonaPrototype> persona)
    {
        return _conversations.GetValueOrDefault(persona.Id);
    }

    /// <summary>
    ///     Stops the backend and starts it again with fresh restart attempts, re-reading every launch cvar.
    /// </summary>
    public void Restart()
    {
        if (!_enabled || _shuttingDown)
            return;

        FailAllTurns();
        _restartAttempt = 0;
        StartBackend();
    }

    private void OnEnabledChanged(bool enabled)
    {
        if (_shuttingDown || enabled == _enabled)
            return;

        _enabled = enabled;
        if (enabled)
        {
            _restartAttempt = 0;
            StartBackend();
            return;
        }

        StopBackend();
        FailAllTurns();
        State = KsLlmState.Disabled;
        _sawmill.Info("Disabled.");
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<KsLlmToolPrototype>() || args.WasModified<KsLlmPersonaPrototype>())
            _personaToolsCache.Clear();
    }

    #region Turns

    private void BeginTurn(KsLlmTurnRequest request)
    {
        if (!_prototypeManager.TryIndex(request.Persona, out var persona))
        {
            InvokeOnComplete(request, KsLlmTurnResult.Failed);
            return;
        }

        if (!_conversations.TryGetValue(persona.ID, out var conversation))
        {
            conversation = new KsLlmConversation(persona.SystemPrompt);
            _conversations[persona.ID] = conversation;
        }

        var turn = new ActiveTurn
        {
            Request = request,
            Persona = persona,
            Conversation = conversation,
            Constrained = _constrainedTools,
        };

        _activeTurn = turn;
        State = KsLlmState.Busy;

        turn.RollbackCount = conversation.Messages.Count;
        conversation.Messages.Add(new KsLlmMessage(KsLlmWireRoles.User, request.UserMessage));

        // Compacted with the new message already in place, so it can always be the start of the kept tail.
        if (NeedsCompaction(conversation) && TryStartCompaction(turn))
            return;

        DispatchCompletion(turn);
    }

    private void DispatchCompletion(ActiveTurn turn)
    {
        turn.Phase = TurnPhase.Completing;
        Send(turn, BuildCompletionRequest(turn));
    }

    private KsLlmWireRequest BuildCompletionRequest(ActiveTurn turn)
    {
        var persona = turn.Persona;
        var personaTools = GetPersonaTools(persona);
        var messages = turn.Conversation.Messages.ToArray();

        if (turn.Constrained)
        {
            // The response shape is described in the system prompt and enforced by the grammar.
            messages[0] = messages[0] with
            {
                Content = messages[0].Content + "\n\n" + (turn.ForcedText ? personaTools.ConstrainedFinalInstructions : personaTools.ConstrainedInstructions),
            };

            return new KsLlmWireRequest
            {
                Messages = messages,
                ResponseFormat = turn.ForcedText ? personaTools.ConstrainedFinalFormat : personaTools.ConstrainedFormat,
                Temperature = persona.Temperature,
                MaxTokens = persona.MaxTokens,
            };
        }

        var hasTools = personaTools.WireTools.Count > 0;
        return new KsLlmWireRequest
        {
            Messages = messages,
            Tools = hasTools ? personaTools.WireTools : null,
            ToolChoice = hasTools && turn.ForcedText ? "none" : null,
            Temperature = persona.Temperature,
            MaxTokens = persona.MaxTokens,
        };
    }

    private void Send(ActiveTurn turn, KsLlmWireRequest request)
    {
        var requestId = ++_lastRequestId;
        turn.RequestId = requestId;

        var httpClient = _httpClient;
        var url = _baseUrl + "/v1/chat/completions";
        var apiKey = _apiKey;
        var timeout = TimeSpan.FromSeconds(_requestTimeout);
        var cancellationToken = _lifetimeCancellationTokenSource.Token;

        _ = Task.Run(() => SendCompletionAsync(requestId, httpClient, url, apiKey, request, timeout, cancellationToken));
    }

    /// <summary>
    ///     Background only. Touches nothing but its arguments and <see cref="_events"/>.
    /// </summary>
    private async Task SendCompletionAsync(int requestId,
        HttpClient httpClient,
        string url,
        string apiKey,
        KsLlmWireRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellationTokenSource.CancelAfter(timeout);

        try
        {
            var requestJson = JsonSerializer.Serialize(request, JsonOptions);
            using var httpRequest = CreateRequest(HttpMethod.Post, url, apiKey);
            httpRequest.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

            using var response = await httpClient.SendAsync(httpRequest, timeoutCancellationTokenSource.Token).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(timeoutCancellationTokenSource.Token).ConfigureAwait(false);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                _events.Enqueue(new CompletionEvent(requestId, null, $"the backend rejected the API key (HTTP {(int)response.StatusCode}); check klovn.llm.api_key.", ContextOverflow: false));
                return;
            }

            if (!response.IsSuccessStatusCode)
            {
                var overflow = body.Contains("context", StringComparison.OrdinalIgnoreCase)
                               && (body.Contains("exceed", StringComparison.OrdinalIgnoreCase)
                                   || body.Contains("too long", StringComparison.OrdinalIgnoreCase));

                _events.Enqueue(new CompletionEvent(requestId, null, $"HTTP {(int)response.StatusCode}: {Truncate(body, 300)}", overflow));
                return;
            }

            var parsed = JsonSerializer.Deserialize<KsLlmWireResponse>(body, JsonOptions);
            _events.Enqueue(new CompletionEvent(requestId, parsed, parsed == null ? "empty response" : null, ContextOverflow: false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down. Nobody is listening.
        }
        catch (OperationCanceledException) when (timeoutCancellationTokenSource.IsCancellationRequested)
        {
            _events.Enqueue(new CompletionEvent(requestId, null, $"timed out after {timeout.TotalSeconds}s", ContextOverflow: false));
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            // Never got an answer at all - refused, unreachable, or the connect timeout. The backend is gone,
            // which is a different thing from it answering badly.
            _events.Enqueue(new CompletionEvent(requestId, null, exception.Message, ContextOverflow: false, ConnectionLost: true));
        }
        catch (Exception exception)
        {
            _events.Enqueue(new CompletionEvent(requestId, null, exception.Message, ContextOverflow: false));
        }
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string url, string apiKey)
    {
        var request = new HttpRequestMessage(method, url);
        if (apiKey.Length > 0)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        return request;
    }

    private void HandleCompletion(CompletionEvent completionEvent)
    {
        var turn = _activeTurn;
        if (turn == null || completionEvent.RequestId != turn.RequestId)
            return;

        // A local process that dies reports through its exit event; a remote backend only through this.
        if (completionEvent.ConnectionLost && _remoteEndpoint)
        {
            _sawmill.Warning($"Lost the remote backend at {_baseUrl}: {completionEvent.Error}");
            ScheduleRestart(retryable: true);
            return;
        }

        if (turn.Phase == TurnPhase.Compacting)
        {
            FinishCompaction(turn, completionEvent);
            return;
        }

        if (completionEvent.Error != null)
        {
            if (completionEvent.ContextOverflow && !turn.RetriedOverflow)
            {
                turn.RetriedOverflow = true;
                _sawmill.Info("Context window exceeded; compacting and retrying once.");
                if (TryStartCompaction(turn))
                    return;
            }

            _sawmill.Warning($"Completion failed: {completionEvent.Error}");
            FailTurn(turn);
            return;
        }

        var response = completionEvent.Response!;
        var message = response.Choices?.FirstOrDefault()?.Message;
        if (message == null)
        {
            _sawmill.Warning("Completion had no message.");
            FailTurn(turn);
            return;
        }

        if (response.Usage is { } usage)
            turn.Conversation.LastTokenCount = usage.PromptTokens + usage.CompletionTokens;

        if (turn.Constrained)
            HandleConstrainedMessage(turn, message);
        else
            HandleNativeMessage(turn, message);
    }

    private void HandleNativeMessage(ActiveTurn turn, KsLlmWireResponseMessage message)
    {
        var messages = turn.Conversation.Messages;

        if (message.ToolCalls is { Count: > 0 } toolCalls && !turn.ForcedText)
        {
            var wireToolCalls = new List<KsLlmWireToolCall>(toolCalls.Count);
            foreach (var toolCall in toolCalls)
            {
                var id = string.IsNullOrEmpty(toolCall.Id) ? $"call_{++_lastToolCallId}" : toolCall.Id;
                var function = new KsLlmWireFunctionCall(toolCall.Function?.Name ?? string.Empty, toolCall.Function?.ArgumentsText ?? string.Empty);
                wireToolCalls.Add(new KsLlmWireToolCall(id, function));
            }

            messages.Add(new KsLlmMessage(KsLlmWireRoles.Assistant, message.Content ?? string.Empty, wireToolCalls));

            // Each result goes straight back into the history for the follow-up request.
            foreach (var wireToolCall in wireToolCalls)
            {
                var result = RunToolCall(turn, wireToolCall.Function.Name, wireToolCall.Function.Arguments);
                messages.Add(new KsLlmMessage(KsLlmWireRoles.Tool, result, ToolCallId: wireToolCall.Id));
            }

            AfterToolRound(turn);
            return;
        }

        var reply = CleanReply(message.Content);
        if (string.IsNullOrEmpty(reply))
        {
            _sawmill.Warning("Completion ended with neither a reply nor a tool call.");
            FailTurn(turn);
            return;
        }

        messages.Add(new KsLlmMessage(KsLlmWireRoles.Assistant, reply));
        CompleteTurn(turn, reply);
    }

    private void HandleConstrainedMessage(ActiveTurn turn, KsLlmWireResponseMessage message)
    {
        var messages = turn.Conversation.Messages;
        var content = message.Content ?? string.Empty;
        messages.Add(new KsLlmMessage(KsLlmWireRoles.Assistant, content));

        if (!TryParseConstrained(content, out var reply, out var toolName, out var argumentsText))
        {
            // The grammar makes this near-impossible; a truncated generation (max_tokens) is the usual cause - and
            // that recurs, since the model tends to write the same long reply again. Once the final answer was
            // already demanded there is no later round to bound this, so the turn ends here.
            if (turn.ForcedText)
            {
                _sawmill.Warning("Final constrained response was not valid JSON; abandoning the turn.");
                FailTurn(turn);
                return;
            }

            messages.Add(new KsLlmMessage(KsLlmWireRoles.User, "[tool result] Error: your last response was not a single valid JSON object of the required shape."));
            AfterToolRound(turn);
            return;
        }

        if (reply != null)
        {
            reply = CleanReply(reply);
            if (string.IsNullOrEmpty(reply))
            {
                FailTurn(turn);
                return;
            }

            CompleteTurn(turn, reply);
            return;
        }

        var result = RunToolCall(turn, toolName!, argumentsText!);
        messages.Add(new KsLlmMessage(KsLlmWireRoles.User, $"[tool result: {toolName}] {result}"));
        AfterToolRound(turn);
    }

    private void AfterToolRound(ActiveTurn turn)
    {
        turn.ToolRounds++;

        // The runaway-loop bound: past it, the next request only allows a text answer.
        if (turn.ToolRounds >= _maxToolTurns)
            turn.ForcedText = true;

        DispatchCompletion(turn);
    }

    /// <summary>
    ///     Validates a tool call the model made and, if it passes, hands it to the turn's executor. Returns the
    ///         model-facing result. Never throws.
    /// </summary>
    private string RunToolCall(ActiveTurn turn, string name, string argumentsText)
    {
        var personaTools = GetPersonaTools(turn.Persona);
        if (!personaTools.ToolsByName.TryGetValue(name, out var tool))
            return $"Error: there is no tool named '{name}'. Available tools: {string.Join(", ", personaTools.ToolsByName.Keys)}.";

        if (!KsLlmToolArguments.TryParse(tool, argumentsText, out var arguments, out var error))
            return "Error: " + error;

        KsLlmToolOutcome outcome;
        try
        {
            outcome = turn.Request.ExecuteTool(new KsLlmToolCall(tool, arguments));
        }
        catch (Exception exception)
        {
            _sawmill.Error($"Tool {tool.ID} threw: {exception}");
            outcome = KsLlmToolOutcome.Error("the action failed due to an internal error.");
        }

        return outcome.Success ? outcome.Message : "Error: " + outcome.Message;
    }

    private void CompleteTurn(ActiveTurn turn, string reply)
    {
        // A backend that has just produced a whole reply is healthy, so its crash budget starts over.
        _restartAttempt = 0;
        EndTurn(turn, new KsLlmTurnResult(true, reply));
    }

    private void FailTurn(ActiveTurn turn)
    {
        // Leave no half-finished exchange behind to confuse the next one.
        if (turn.RollbackCount >= 0 && turn.RollbackCount <= turn.Conversation.Messages.Count)
            turn.Conversation.Messages.RemoveRange(turn.RollbackCount, turn.Conversation.Messages.Count - turn.RollbackCount);

        EndTurn(turn, KsLlmTurnResult.Failed);
    }

    private void EndTurn(ActiveTurn turn, KsLlmTurnResult result)
    {
        if (_activeTurn == turn)
        {
            _activeTurn = null;
            if (State == KsLlmState.Busy)
                State = KsLlmState.Ready;
        }

        InvokeOnComplete(turn.Request, result);
    }

    private void FailAllTurns()
    {
        if (_activeTurn is { } turn)
            FailTurn(turn);

        while (_queuedTurns.TryDequeue(out var request))
            InvokeOnComplete(request, KsLlmTurnResult.Failed);
    }

    private void InvokeOnComplete(KsLlmTurnRequest request, KsLlmTurnResult result)
    {
        try
        {
            request.OnComplete(result);
        }
        catch (Exception exception)
        {
            _sawmill.Error($"Turn completion callback threw: {exception}");
        }
    }

    #endregion

    #region Helpers

    private static string CleanReply(string? content)
    {
        if (string.IsNullOrEmpty(content))
            return string.Empty;

        return ThinkBlockRegex.Replace(content, string.Empty).Trim();
    }

    private static bool TryParseConstrained(string content, out string? reply, out string? toolName, out string? argumentsText)
    {
        reply = null;
        toolName = null;
        argumentsText = null;

        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            if (root.TryGetProperty("reply", out var replyElement) && replyElement.ValueKind == JsonValueKind.String)
            {
                reply = replyElement.GetString();
                return true;
            }

            if (root.TryGetProperty("tool", out var toolElement) && toolElement.ValueKind == JsonValueKind.String)
            {
                toolName = toolElement.GetString();
                argumentsText = root.TryGetProperty("args", out var argumentsElement) ? argumentsElement.GetRawText() : "{}";
                return true;
            }

            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Truncate(string text, int length)
    {
        return text.Length <= length ? text : text[..length] + "…";
    }

    #endregion

    #region Types

    private enum TurnPhase : byte
    {
        Completing,
        Compacting,
    }

    private sealed class ActiveTurn
    {
        public required KsLlmTurnRequest Request;
        public required KsLlmPersonaPrototype Persona;
        public required KsLlmConversation Conversation;

        /// <summary>
        ///     Whether this turn uses the grammar-constrained response format. Fixed for the turn's lifetime.
        /// </summary>
        public required bool Constrained;

        /// <summary>
        ///     Index of this turn's user message in the history. Everything from here on is removed if the turn
        ///         fails, and compaction never reaches past it.
        /// </summary>
        public int RollbackCount = -1;

        public int RequestId;
        public TurnPhase Phase;
        public int ToolRounds;
        public bool ForcedText;
        public bool RetriedOverflow;

        /// <summary>
        ///     While compacting: where the kept tail of the history starts.
        /// </summary>
        public int CompactionCut;
    }

    private abstract record BackendEvent;

    private sealed record CompletionEvent(
        int RequestId,
        KsLlmWireResponse? Response,
        string? Error,
        bool ContextOverflow,
        bool ConnectionLost = false) : BackendEvent;

    #endregion
}
