using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Llm.Prototypes;

/// <summary>
///     A character the LLM plays: its standing instructions and the tools it may call.
/// </summary>
/// <remarks>
///     The prompts are model-facing, not player-facing, so they are plain YAML rather than localized strings.
/// </remarks>
[Prototype]
public sealed partial class KsLlmPersonaPrototype : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    ///     The system prompt that opens every conversation with this persona.
    /// </summary>
    [DataField(required: true)]
    public string SystemPrompt = string.Empty;

    /// <summary>
    ///     Instruction given to the model when it is asked to summarise its own older history.
    /// </summary>
    [DataField]
    public string CompactionPrompt =
        "Summarise the conversation so far in a few short paragraphs. Keep every name, request, promise, "
        + "decision and action taken, and anything you would need to stay consistent later. Reply with the "
        + "summary only.";

    /// <summary>
    ///     Tools this persona may call. The model is offered exactly these and nothing else.
    /// </summary>
    [DataField]
    public List<ProtoId<KsLlmToolPrototype>> Tools = new();

    /// <summary>
    ///     A final reply longer than this many characters is cut short.
    /// </summary>
    [DataField]
    public int MaxReplyLength = 1500;

    /// <summary>
    ///     Sampling temperature sent with every request.
    /// </summary>
    [DataField]
    public float Temperature = 0.7f;

    /// <summary>
    ///     Upper bound on generated tokens per request.
    /// </summary>
    [DataField]
    public int MaxTokens = 512;
}
