using System.Linq;
using System.Text;
using Content.Server._KS14.Fax;
using Content.Server._KS14.Llm.Prototypes;
using Content.Server._KS14.Llm.Tools;
using Content.Server.Administration.Logs;
using Content.Server.Chat.Managers;
using Content.Server.Fax;
using Content.Server.Station.Systems;
using Content.Shared._KS14.CCVar;
using Content.Shared.Database;
using Content.Shared.DeviceNetwork.Components;
using Content.Shared.Fax.Components;
using Content.Shared.GameTicking;
using Content.Shared.Paper;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._KS14.Llm.Fax;

/// <summary>
///     Turns a fax to a <see cref="KsLlmFaxRecipientComponent"/> machine into a turn with its persona - the paper's
///         text and stamps are the prompt - and faxes the persona's reply back to the sender.
/// </summary>
/// <remarks>
///     When the backend is off, down or busy past its queue, nothing happens at all: the fax arrives exactly as
///         upstream, admins are pinged as upstream, and a human can answer it.
/// </remarks>
public sealed partial class KsLlmFaxSystem : EntitySystem
{
    [Dependency] private KsLlmManager _llmManager = default!;
    [Dependency] private FaxSystem _faxSystem = default!;
    [Dependency] private StationSystem _stationSystem = default!;
    [Dependency] private IAdminLogManager _adminLogManager = default!;
    [Dependency] private IChatManager _chatManager = default!;
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private IGameTiming _gameTiming = default!;

    /// <summary>
    ///     Successful uses of each tool this round, keyed by tool prototype ID.
    /// </summary>
    private readonly Dictionary<string, ToolUsage> _toolUsages = new();

    private int _maxInputChars;

    /// <summary>
    ///     Bumped on every round restart. A turn remembers the round it was started in, and nothing it does -
    ///         tool calls, the reply - lands in any other.
    /// </summary>
    private int _round;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_configurationManager, KsCCVars.LlmMaxInputChars, value => _maxInputChars = value, invokeImmediately: true);
    }

    [SubscribeLocalEvent]
    private void OnRoundRestartCleanup(RoundRestartCleanupEvent args)
    {
        _round++;

        // Before the history goes, so nothing from the old round is still running against it.
        _llmManager.CancelAllTurns();
        _llmManager.ResetConversation();
        _toolUsages.Clear();
    }

    [SubscribeLocalEvent]
    private void OnFaxReceived(Entity<KsLlmFaxRecipientComponent> entity, ref KsFaxReceivedEvent args)
    {
        if (_llmManager.State is not (KsLlmState.Ready or KsLlmState.Busy))
            return;

        // Nowhere to send a reply, or a reply from another persona: answering the latter would loop forever.
        if (args.FromAddress is not { } fromAddress
            || !TryFindFax(fromAddress, out var senderFaxUid)
            || HasComp<KsLlmFaxRecipientComponent>(senderFaxUid))
            return;

        var recipientFaxUid = entity.Owner;
        var persona = entity.Comp.Persona;
        var stationUid = _stationSystem.GetOwningStation(senderFaxUid);
        var stamps = args.Printout.StampedBy.ToList();
        var round = _round;

        var request = new KsLlmTurnRequest
        {
            Persona = persona,
            UserMessage = BuildUserMessage(args.Printout, senderFaxUid, stationUid),
            ExecuteTool = toolCall => round == _round
                ? ExecuteTool(toolCall, recipientFaxUid, senderFaxUid, stationUid, stamps)
                : KsLlmToolOutcome.Error("this request belongs to a previous shift and can no longer be acted on."),
            OnComplete = result =>
            {
                if (round == _round)
                    OnTurnComplete(result, recipientFaxUid, senderFaxUid, persona);
            },
        };

        if (_llmManager.TryRequestTurn(request))
            _adminLogManager.Add(LogType.Action, LogImpact.Low, $"{ToPrettyString(senderFaxUid):subject} faxed {ToPrettyString(recipientFaxUid):tool}, which was passed to LLM persona {persona}.");
    }

    private string BuildUserMessage(FaxPrintout printout, EntityUid senderFaxUid, EntityUid? stationUid)
    {
        var senderName = CompOrNull<FaxMachineComponent>(senderFaxUid)?.FaxName ?? printout.SenderFaxName ?? "unknown";
        var stationName = stationUid is { } station ? Name(station) : "no station";

        // Papers are written in markup; the model only needs the words.
        var content = FormattedMessage.RemoveMarkupPermissive(printout.Content).Trim();
        if (content.Length > _maxInputChars)
            content = content[.._maxInputChars] + " [truncated]";

        var builder = new StringBuilder();
        builder.AppendLine($"Fax received from: {senderName} ({stationName})");
        builder.AppendLine($"Title: {printout.Name}");

        builder.Append("Stamps: ");
        if (printout.StampedBy.Count == 0)
            builder.AppendLine("none");
        else
            builder.AppendLine(string.Join(", ", printout.StampedBy.Select(stamp => $"{GetStampName(stamp)} ({stamp.StampedColor.ToHex()})")));

        builder.AppendLine("Content:");
        builder.Append(content.Length == 0 ? "(blank)" : content);
        return builder.ToString();
    }

    private string GetStampName(StampDisplayInfo stamp)
    {
        // Real stamps store a locale ID; admin faxes store whatever the admin typed.
        return Loc.TryGetString(stamp.StampedName, out var name) ? name : stamp.StampedName;
    }

    private KsLlmToolOutcome ExecuteTool(KsLlmToolCall toolCall,
        EntityUid recipientFaxUid,
        EntityUid senderFaxUid,
        EntityUid? stationUid,
        IReadOnlyList<StampDisplayInfo> stamps)
    {
        var tool = toolCall.Tool;
        var outcome = CheckUsage(tool) ?? tool.Effect.Execute(new KsLlmToolContext
        {
            EntityManager = EntityManager,
            Localization = Loc,
            Arguments = toolCall.Arguments,
            RecipientFaxUid = recipientFaxUid,
            SenderFaxUid = Exists(senderFaxUid) ? senderFaxUid : null,
            StationUid = stationUid is { } station && Exists(station) ? station : null,
            Stamps = stamps,
        });

        if (outcome.Success)
        {
            var toolUsage = _toolUsages.GetValueOrDefault(tool.ID);
            _toolUsages[tool.ID] = new ToolUsage(toolUsage.Uses + 1, _gameTiming.CurTime);

            if (tool.NotifyAdmins)
                _chatManager.SendAdminAnnouncement(Loc.GetString("ks-llm-admin-tool-used", ("tool", tool.Name), ("result", outcome.Message)));
        }

        _adminLogManager.Add(LogType.Action,
            tool.LogImpact,
            $"LLM tool {tool.Name} for a fax from {ToPrettyString(senderFaxUid):subject} {(outcome.Success ? "succeeded" : "was refused")}: {outcome.Message}");

        return outcome;
    }

    /// <summary>
    ///     The refusal, if this tool has hit its per-round limit or is cooling down.
    /// </summary>
    private KsLlmToolOutcome? CheckUsage(KsLlmToolPrototype tool)
    {
        if (!_toolUsages.TryGetValue(tool.ID, out var toolUsage))
            return null;

        if (tool.MaxUsesPerRound is { } maxUses && toolUsage.Uses >= maxUses)
            return KsLlmToolOutcome.Error($"'{tool.Name}' may only be used {maxUses} time(s) per shift, and that is used up.");

        var readyAt = toolUsage.LastUse + tool.Cooldown;
        if (_gameTiming.CurTime < readyAt)
            return KsLlmToolOutcome.Error($"'{tool.Name}' is not available again for {(int)Math.Ceiling((readyAt - _gameTiming.CurTime).TotalSeconds)} seconds.");

        return null;
    }

    private void OnTurnComplete(KsLlmTurnResult result,
        EntityUid recipientFaxUid,
        EntityUid senderFaxUid,
        ProtoId<KsLlmPersonaPrototype> persona)
    {
        if (!result.Success || result.Reply is not { } reply)
            return;

        // Either end may have gone - deconstructed, or the round restarted - while the model was thinking.
        if (!TryComp<KsLlmFaxRecipientComponent>(recipientFaxUid, out var recipientComponent)
            || !TryComp<FaxMachineComponent>(recipientFaxUid, out var recipientFaxComponent)
            || !HasComp<FaxMachineComponent>(senderFaxUid))
            return;

        var maxLength = ProtoMan.TryIndex(persona, out var personaPrototype) ? personaPrototype.MaxReplyLength : reply.Length;
        if (reply.Length > maxLength)
            reply = reply[..maxLength];

        var printout = new FaxPrintout(FormattedMessage.EscapeText(reply),
            Loc.GetString(recipientComponent.ReplyTitle),
            stampState: recipientComponent.ReplyStampState,
            stampedBy: new List<StampDisplayInfo>(recipientComponent.ReplyStampedBy),
            senderFaxName: recipientFaxComponent.FaxName);

        var recipientAddress = CompOrNull<DeviceNetworkComponent>(recipientFaxUid)?.Address;
        _faxSystem.Receive(senderFaxUid, printout, recipientAddress);

        _adminLogManager.Add(LogType.Action, LogImpact.Low, $"LLM persona {persona} replied by fax to {ToPrettyString(senderFaxUid):subject}: {reply}");
    }

    private bool TryFindFax(string address, out EntityUid faxUid)
    {
        var faxQuery = EntityQueryEnumerator<FaxMachineComponent, DeviceNetworkComponent>();
        while (faxQuery.MoveNext(out var uid, out _, out var deviceNetworkComponent))
        {
            if (deviceNetworkComponent.Address != address)
                continue;

            faxUid = uid;
            return true;
        }

        faxUid = default;
        return false;
    }

    private readonly record struct ToolUsage(int Uses, TimeSpan LastUse);
}
