using Content.Server.Administration;
using Content.Server._KS14.Llm.Prototypes;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Llm.Commands;

/// <summary>
///     Inspects and controls the LLM backend: <c>ks_llm status|restart|reset</c>.
/// </summary>
[AdminCommand(AdminFlags.Host)]
public sealed partial class KsLlmCommand : LocalizedCommands
{
    [Dependency] private KsLlmManager _llmManager = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;

    public override string Command => "ks_llm";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError(Loc.GetString("cmd-ks_llm-invalid-args"));
            return;
        }

        switch (args[0])
        {
            case "status":
                WriteStatus(shell);
                break;
            case "restart":
                _llmManager.Restart();
                shell.WriteLine(Loc.GetString("cmd-ks_llm-restarting"));
                break;
            case "reset":
                _llmManager.ResetConversation();
                shell.WriteLine(Loc.GetString("cmd-ks_llm-reset"));
                break;
            default:
                shell.WriteError(Loc.GetString("cmd-ks_llm-invalid-args"));
                break;
        }
    }

    private void WriteStatus(IConsoleShell shell)
    {
        shell.WriteLine(Loc.GetString("cmd-ks_llm-status",
            ("state", _llmManager.State.ToString()),
            ("url", string.IsNullOrEmpty(_llmManager.BackendUrl) ? "-" : _llmManager.BackendUrl),
            ("context", _llmManager.ContextSize),
            ("queued", _llmManager.QueuedTurnCount),
            ("inFlight", _llmManager.TurnInFlight.ToString())));

        foreach (var persona in _prototypeManager.EnumeratePrototypes<KsLlmPersonaPrototype>())
        {
            if (_llmManager.GetConversation(persona.ID) is not { } conversation)
                continue;

            shell.WriteLine(Loc.GetString("cmd-ks_llm-status-conversation",
                ("persona", persona.ID),
                ("messages", conversation.Messages.Count),
                ("tokens", conversation.LastTokenCount),
                ("compactions", conversation.Compactions)));
        }
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 1
            ? CompletionResult.FromOptions(["status", "restart", "reset"])
            : CompletionResult.Empty;
    }
}
