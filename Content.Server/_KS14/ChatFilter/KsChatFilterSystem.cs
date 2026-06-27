using Content.Server.Chat.Managers;
using Content.Server.Chat.Systems;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.WordFilter;
using Content.Shared.Chat;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server._KS14.ChatFilter;

public sealed class KsChatFilterSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _configurationManager = default!;
    [Dependency] private readonly IChatManager _chatManager = default!;
    [Dependency] private readonly WordFilterSystem _wordFilterSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        if (_configurationManager.GetCVar(KsCCVars.WordFilterEnabled))
        {
            SubscribeLocalEvent<KsBeforeMessageSentEvent>(OnBeforeMessageSent);
            SubscribeLocalEvent<KsSanitiseMessageEvent>(OnSanitiseMessage);
        }
    }

    // OOC and IC; prohibit words
    private void OnBeforeMessageSent(ref KsBeforeMessageSentEvent args)
    {
        if (args.Cancelled)
            return;

        var message = WordFilterSystem.SkeletoniseString(WordFilterSystem.ParseToLatin(args.Message));
        if (!_wordFilterSystem.AnyFilterMatches(message, WordFilterCategory.Prohibited))
            return;

        SendMessage(args.Session, "ks-word-filter-prohibited");
        args.Cancelled = true;
    }

    // IC only; modify words
    private void OnSanitiseMessage(ref KsSanitiseMessageEvent args)
    {
        var message = WordFilterSystem.SkeletoniseString(WordFilterSystem.ParseToLatin(args.Message));

        var originalMessage = message;
        _wordFilterSystem.FilterAndReplaceString(ref message, WordFilterCategory.Normal);

        if (originalMessage == message)
            return;

        SendMessage(args.Session, "ks-word-filter-warn");
        args.Message = message;
    }

    private void SendMessage(ICommonSession session, LocId locId)
    {
        var message = Loc.GetString(locId);
        var wrappedMessage = Loc.GetString("chat-manager-server-wrap-message", ("message", FormattedMessage.EscapeText(message)));
        _chatManager.ChatMessageToOne(ChatChannel.Server, message, wrappedMessage, default, false, session!.Channel, colorOverride: Color.Purple);
    }
}
