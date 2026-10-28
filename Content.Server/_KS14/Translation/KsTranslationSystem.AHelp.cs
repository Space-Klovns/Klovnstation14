using System;
using System.Diagnostics.CodeAnalysis;
using Content.Shared._KS14.CCVar;
using Content.Shared.Chat;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._KS14.Translation;

public sealed partial class KsTranslationSystem
{
    // UPSTREAM-BS-TRANSLATION-001: no private conversation is ever added to shared chat context.
    private const string KsAHelpContext =
        "A private support conversation between a player and a game administrator in a sci-fi space station game.";

    public bool KsTryBeginAHelp(string plain, ICommonSession speaker, [NotNullWhen(true)] out KsTranslationContext? context)
    {
        var language = _netCfg.GetClientCVar(speaker.Channel, KsCCVars.TranslateLanguage);
        return TryBeginCommon(ChatChannel.AdminChat, plain, language, speaker.UserId, out context, ksContextOverride: KsAHelpContext);
    }

    public bool KsTryAHelpReader(string plain, KsTranslationContext context, INetChannel reader,
        Action<int, INetChannel, string> deliver)
    {
        if (!TryResolveTarget(context, reader, out var target))
            return false;

        Enqueue(++_nextMessageId, reader, plain, context, target, ksDeliver: deliver, ksCacheScope: "ahelp:");
        return true;
    }
}
