using Content.Shared.Administration;
using Content.Shared.CCVar.CVarAccess;
using Robust.Shared.Configuration;

namespace Content.Shared._KS14.CCVar;

/// <summary>
///     CVars for the local LLM correspondent (server-side <c>KsLlmManager</c>). Every one of these is read by
///         server code only, hence <see cref="CVar.SERVERONLY"/>.
/// </summary>
/// <remarks>
///     Resource budget, for sizing the host: a 7-8B instruct model at Q4_K_M is ~4.5-5 GB on disk and needs
///         roughly 5.5-6.5 GB of VRAM with every layer offloaded and a 4096-token context, or the same in system
///         RAM (and much slower generation) at <see cref="LlmGpuLayers"/> = 0. That is on top of whatever the
///         game server itself uses, on the same box.
/// </remarks>
public sealed partial class KsCCVars
{
    /// <summary>
    ///     Master switch. When false the backend is not started, and faxes to Central Command behave exactly
    ///         as upstream. Toggling it at runtime starts or stops the backend.
    /// </summary>
    [CVarControl(AdminFlags.Host)]
    public static readonly CVarDef<bool> LlmEnabled =
        CVarDef.Create("klovn.llm.enabled", false, CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Path to the <c>llama-server</c> executable. Ignored when <see cref="LlmEndpoint"/> is set.
    /// </summary>
    [CVarControl(AdminFlags.Host)]
    public static readonly CVarDef<string> LlmServerPath =
        CVarDef.Create("klovn.llm.server_path", "", CVar.SERVERONLY | CVar.ARCHIVE | CVar.CONFIDENTIAL);

    /// <summary>
    ///     Path to the GGUF model file handed to <c>llama-server</c>. Not in source control; see the remarks on
    ///         <see cref="KsCCVars"/> for sizing. Ignored when <see cref="LlmEndpoint"/> is set.
    /// </summary>
    [CVarControl(AdminFlags.Host)]
    public static readonly CVarDef<string> LlmModelPath =
        CVarDef.Create("klovn.llm.model_path", "", CVar.SERVERONLY | CVar.ARCHIVE | CVar.CONFIDENTIAL);

    /// <summary>
    ///     When non-empty, the base URL of an already-running OpenAI-compatible server - on this machine or
    ///         another one (e.g. <c>http://203.0.113.7:8080</c>). No child process is spawned or managed in that
    ///         case. Unlike a local process, a remote backend is never given up on: while it is unreachable the
    ///         manager keeps retrying, at most a minute apart, and picks it back up as soon as it answers.
    /// </summary>
    [CVarControl(AdminFlags.Host)]
    public static readonly CVarDef<string> LlmEndpoint =
        CVarDef.Create("klovn.llm.endpoint", "", CVar.SERVERONLY | CVar.ARCHIVE | CVar.CONFIDENTIAL);

    /// <summary>
    ///     Sent as <c>Authorization: Bearer &lt;key&gt;</c> with every request. Set it to the <c>--api-key</c> the
    ///         remote llama-server was started with. Plain HTTP sends it unencrypted, so restrict who can reach the
    ///         endpoint at the firewall as well; the key alone is not enough.
    /// </summary>
    [CVarControl(AdminFlags.Host)]
    public static readonly CVarDef<string> LlmApiKey =
        CVarDef.Create("klovn.llm.api_key", "", CVar.SERVERONLY | CVar.ARCHIVE | CVar.CONFIDENTIAL);

    /// <summary>
    ///     <c>--n-gpu-layers</c>. 0 for CPU-only hosts; a large number (e.g. 999) offloads every layer.
    /// </summary>
    [CVarControl(AdminFlags.Host)]
    public static readonly CVarDef<int> LlmGpuLayers =
        CVarDef.Create("klovn.llm.gpu_layers", 0, CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    ///     <c>--ctx-size</c>. Also the fallback context size for compaction when the backend's <c>/props</c>
    ///         cannot be read.
    /// </summary>
    [CVarControl(AdminFlags.Host)]
    public static readonly CVarDef<int> LlmContextSize =
        CVarDef.Create("klovn.llm.ctx_size", 4096, CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Extra command-line arguments appended verbatim to the <c>llama-server</c> launch, space-separated.
    /// </summary>
    [CVarControl(AdminFlags.Host)]
    public static readonly CVarDef<string> LlmExtraArgs =
        CVarDef.Create("klovn.llm.extra_args", "", CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Seconds to wait for the backend's <c>/health</c> to answer before a start attempt counts as failed.
    /// </summary>
    public static readonly CVarDef<float> LlmStartupTimeout =
        CVarDef.Create("klovn.llm.startup_timeout", 30f, CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Seconds a single completion request may take before it is abandoned.
    /// </summary>
    public static readonly CVarDef<float> LlmRequestTimeout =
        CVarDef.Create("klovn.llm.request_timeout", 120f, CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    ///     How many times a crashed or failed backend is restarted, with exponential backoff, before the
    ///         manager gives up and stays failed until restarted by hand.
    /// </summary>
    public static readonly CVarDef<int> LlmRestartAttempts =
        CVarDef.Create("klovn.llm.restart_attempts", 3, CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Maximum number of tool-calling round trips in one turn. Past it the model is made to answer in text.
    /// </summary>
    [CVarControl(AdminFlags.Server)]
    public static readonly CVarDef<int> LlmMaxToolTurns =
        CVarDef.Create("klovn.llm.max_tool_turns", 5, CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Maximum number of turns waiting behind the one in flight. Anything past it is dropped.
    /// </summary>
    [CVarControl(AdminFlags.Server)]
    public static readonly CVarDef<int> LlmMaxQueuedTurns =
        CVarDef.Create("klovn.llm.max_queued_turns", 8, CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Paper content longer than this many characters is truncated before it is put in a prompt.
    /// </summary>
    [CVarControl(AdminFlags.Server)]
    public static readonly CVarDef<int> LlmMaxInputChars =
        CVarDef.Create("klovn.llm.max_input_chars", 2000, CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Whether the conversation is summarised in place once it approaches the context window.
    /// </summary>
    [CVarControl(AdminFlags.Server)]
    public static readonly CVarDef<bool> LlmAutoCompact =
        CVarDef.Create("klovn.llm.autocompact", true, CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Fraction of the context window the last request may fill before the next one compacts first.
    /// </summary>
    [CVarControl(AdminFlags.Server)]
    public static readonly CVarDef<float> LlmCompactThreshold =
        CVarDef.Create("klovn.llm.compact_threshold", 0.75f, CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    ///     How many of the most recent messages survive compaction verbatim.
    /// </summary>
    [CVarControl(AdminFlags.Server)]
    public static readonly CVarDef<int> LlmCompactKeepMessages =
        CVarDef.Create("klovn.llm.compact_keep_messages", 4, CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    ///     When true, tool calls are not requested through the model's native tool-calling format but through a
    ///         JSON-schema constrained response, which the backend compiles to a GBNF grammar. Malformed tool-call
    ///         JSON is then structurally impossible. Use if the model's native tool calls prove unreliable.
    /// </summary>
    [CVarControl(AdminFlags.Server)]
    public static readonly CVarDef<bool> LlmConstrainedTools =
        CVarDef.Create("klovn.llm.constrained_tools", false, CVar.SERVERONLY | CVar.ARCHIVE);
}
