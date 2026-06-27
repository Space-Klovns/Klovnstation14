using System.Text;
using Content.Server.Chat.Systems;
using Content.Shared._KS14.CCVar;
using Robust.Shared.Configuration;

namespace Content.Server._KS14.ChatFilter;

/// <inheritdoc cref="KsCCVars.ChatQuotesEnabled"/>
public sealed class KsChatQuoteSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _configurationManager = default!;

    public override void Initialize()
    {
        base.Initialize();

        if (!_configurationManager.GetCVar(KsCCVars.ChatQuotesEnabled))
            return;

        SubscribeLocalEvent<KsBeforeMessageSentEvent>(OnBeforeMessageSent, after: [typeof(KsChatFilterSystem)]);
    }

    private void OnBeforeMessageSent(ref KsBeforeMessageSentEvent args)
    {
        if (args.Cancelled)
            return;

        var message = new StringBuilder(args.Message);
        var closing = false;
        for (var i = 0; i < message.Length; i++)
        {
            var character = message[i];

            if (character != '\"')
                continue;

            message[i] = closing ?
                '”' :
                '“';

            closing = !closing;
        }

        args.Message = message.ToString();
    }
}
