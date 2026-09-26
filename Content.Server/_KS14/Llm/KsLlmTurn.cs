using Content.Server._KS14.Llm.Prototypes;
using Content.Server._KS14.Llm.Tools;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Llm;

public enum KsLlmState : byte
{
    /// <summary>
    ///     The feature is switched off.
    /// </summary>
    Disabled,

    /// <summary>
    ///     The backend is being launched or waited on.
    /// </summary>
    Starting,

    /// <summary>
    ///     The backend answered its health check and nothing is in flight.
    /// </summary>
    Ready,

    /// <summary>
    ///     A turn is in flight. New turns queue behind it.
    /// </summary>
    Busy,

    /// <summary>
    ///     The backend died or failed to start, and another attempt is scheduled.
    /// </summary>
    Restarting,

    /// <summary>
    ///     Every restart attempt was used up. Stays here until restarted by hand or re-enabled.
    /// </summary>
    Failed,
}

/// <summary>
///     One player-triggered exchange with the model: a message in, any number of tool calls, a reply out.
/// </summary>
/// <remarks>
///     Both callbacks run on the main thread, from <see cref="KsLlmManager.Update"/>, and never from a
///         background task - so they may touch entities freely.
/// </remarks>
public sealed class KsLlmTurnRequest
{
    public required ProtoId<KsLlmPersonaPrototype> Persona;

    public required string UserMessage;

    /// <summary>
    ///     Runs a tool call whose arguments have already been validated against the tool's parameters.
    /// </summary>
    public required Func<KsLlmToolCall, KsLlmToolOutcome> ExecuteTool;

    /// <summary>
    ///     Called exactly once, when the turn ends for any reason - including the feature being disabled and
    ///         the backend dying mid-turn.
    /// </summary>
    public required Action<KsLlmTurnResult> OnComplete;
}

/// <summary>
///     A validated call to <paramref name="Tool"/>.
/// </summary>
public readonly record struct KsLlmToolCall(KsLlmToolPrototype Tool, KsLlmToolArguments Arguments);

/// <summary>
///     What a tool call did, as told back to the model.
/// </summary>
public readonly record struct KsLlmToolOutcome(bool Success, string Message)
{
    public static KsLlmToolOutcome Ok(string message) => new(true, message);

    public static KsLlmToolOutcome Error(string message) => new(false, message);
}

/// <summary>
///     How a turn ended. <see cref="Reply"/> is set only on success.
/// </summary>
public readonly record struct KsLlmTurnResult(bool Success, string? Reply)
{
    public static KsLlmTurnResult Failed => new(false, null);
}
