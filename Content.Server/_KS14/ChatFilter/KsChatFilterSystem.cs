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
            SubscribeLocalEvent<KsBeforeMessageSent>(OnBeforeMessageSent);
    }

    private void OnBeforeMessageSent(ref KsBeforeMessageSent args)
    {
        var message = WordFilterSystem.SkeletoniseString(WordFilterSystem.ParseToLatin(args.Message));

        var originalMessage = message;
        _wordFilterSystem.FilterAndReplaceString(ref message, WordFilterCategory.Normal);
        _wordFilterSystem.FilterAndReplaceString(ref message, WordFilterCategory.Slur);

        if (originalMessage == message)
            return;

        Warn(args.Session);
        args.Message = message;
    }

    private void Warn(ICommonSession session)
    {
        var message = Loc.GetString("ks-word-filter-warn");
        var wrappedMessage = Loc.GetString("chat-manager-server-wrap-message", ("message", FormattedMessage.EscapeText(message)));
        _chatManager.ChatMessageToOne(ChatChannel.Server, message, wrappedMessage, default, false, session!.Channel, colorOverride: Color.Purple);
    }
}
