using System.Linq;
using System.Text;

namespace Content.Server._KS14.Llm;

public sealed partial class KsLlmManager
{
    private const string SummaryHeader = "Summary of the earlier correspondence:";

    /// <summary>
    ///     Whether the last request filled enough of the context window that the next should compact first.
    /// </summary>
    private bool NeedsCompaction(KsLlmConversation conversation)
    {
        return _autoCompact
               && conversation.LastTokenCount > _contextSize * _compactThreshold
               && conversation.Messages.Count - 1 > _compactKeepMessages;
    }

    /// <summary>
    ///     Asks the model to summarise the older part of the history, to be folded into the system prompt when it
    ///         answers. Returns false when there is nothing old enough to summarise.
    /// </summary>
    /// <remarks>
    ///     The kept tail always starts on a user message: that keeps an assistant's tool calls together with
    ///         their results, and keeps chat templates that insist on user/assistant alternation happy. It never
    ///         reaches into the turn in flight either, so that turn can still be rolled back cleanly.
    /// </remarks>
    private bool TryStartCompaction(ActiveTurn turn)
    {
        var conversation = turn.Conversation;
        var messages = conversation.Messages;
        var cut = Math.Max(1, messages.Count - Math.Max(1, _compactKeepMessages));
        if (turn.RollbackCount >= 0)
            cut = Math.Min(cut, turn.RollbackCount);

        // Walk back to the nearest user message, so the kept tail opens with one.
        while (cut > 1 && messages[cut].Role != KsLlmWireRoles.User)
            cut--;

        if (cut <= 1)
            return false;

        var transcript = new StringBuilder();
        if (conversation.Summary != null)
            transcript.AppendLine(SummaryHeader).AppendLine(conversation.Summary).AppendLine();

        for (var i = 1; i < cut; i++)
            AppendTranscriptLine(transcript, messages[i]);

        turn.Phase = TurnPhase.Compacting;
        turn.CompactionCut = cut;

        _sawmill.Info($"Compacting {cut - 1} messages ({turn.Conversation.LastTokenCount}/{_contextSize} tokens used).");

        Send(turn, new KsLlmWireRequest
        {
            Messages =
            [
                new KsLlmMessage(KsLlmWireRoles.System, turn.Persona.CompactionPrompt),
                new KsLlmMessage(KsLlmWireRoles.User, transcript.ToString()),
            ],
            Temperature = 0.2f,
            MaxTokens = turn.Persona.MaxTokens,
        });

        return true;
    }

    private void FinishCompaction(ActiveTurn turn, CompletionEvent completionEvent)
    {
        var conversation = turn.Conversation;
        var messages = conversation.Messages;
        var cut = turn.CompactionCut;

        // The history can only have been reset under us, never grown, while a compaction was out.
        if (cut > messages.Count)
        {
            FailTurn(turn);
            return;
        }

        var summary = completionEvent.Error == null
            ? CleanReply(completionEvent.Response?.Choices?.FirstOrDefault()?.Message?.Content)
            : string.Empty;

        if (string.IsNullOrEmpty(summary))
        {
            // Fall back to forgetting the oldest part outright, keeping whatever summary there already was. A
            // new summary would have been better, but a conversation that no longer fits cannot go on at all.
            _sawmill.Warning($"Compaction failed ({completionEvent.Error ?? "empty summary"}); dropping {cut - 1} oldest messages instead.");
        }
        else
        {
            conversation.Summary = summary;
        }

        var systemPrompt = turn.Persona.SystemPrompt;
        if (conversation.Summary != null)
            systemPrompt += $"\n\n{SummaryHeader}\n{conversation.Summary}";

        messages.RemoveRange(0, cut);
        messages.Insert(0, new KsLlmMessage(KsLlmWireRoles.System, systemPrompt));

        if (turn.RollbackCount >= 0)
            turn.RollbackCount -= cut - 1;

        conversation.LastTokenCount = 0;
        conversation.Compactions++;

        DispatchCompletion(turn);
    }

    private static void AppendTranscriptLine(StringBuilder transcript, KsLlmMessage message)
    {
        switch (message.Role)
        {
            case KsLlmWireRoles.User:
                transcript.Append("Incoming: ").AppendLine(message.Content);
                break;
            case KsLlmWireRoles.Assistant:
                if (!string.IsNullOrWhiteSpace(message.Content))
                    transcript.Append("You: ").AppendLine(message.Content);

                if (message.ToolCalls != null)
                {
                    foreach (var toolCall in message.ToolCalls)
                        transcript.Append("You used tool ").Append(toolCall.Function.Name).Append(' ').AppendLine(toolCall.Function.Arguments);
                }

                break;
            case KsLlmWireRoles.Tool:
                transcript.Append("Tool result: ").AppendLine(message.Content);
                break;
        }
    }
}
