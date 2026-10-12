namespace Content.Server._KS14.Llm;

/// <summary>
///     The running history with one persona. Main-thread only; requests are sent from a copy of
///         <see cref="Messages"/>, never from the list itself.
/// </summary>
public sealed class KsLlmConversation
{
    /// <summary>
    ///     Always starts with the system message.
    /// </summary>
    public readonly List<KsLlmMessage> Messages = new();

    /// <summary>
    ///     Prompt plus completion tokens of the last completed request, or 0 when unknown. This is how full
    ///         the context window was, which is what compaction is decided on.
    /// </summary>
    public int LastTokenCount;

    /// <summary>
    ///     The model's own summary of everything compacted away so far, folded into the system message. Null
    ///         until the first successful compaction.
    /// </summary>
    public string? Summary;

    /// <summary>
    ///     How many times the history has been compacted. Diagnostics only.
    /// </summary>
    public int Compactions;

    public KsLlmConversation(string systemPrompt)
    {
        Messages.Add(new KsLlmMessage(KsLlmWireRoles.System, systemPrompt));
    }
}
