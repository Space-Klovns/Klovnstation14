using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.RadioLocalization;

namespace Content.Client.UserInterface.Systems.Chat;

public sealed partial class ChatUIController
{
    private string NormalizeLocalizedRadioPrefix(string text)
    {
        return KsRadioLocalization.NormalizePrefix(_prototypeManager, _config.GetCVar(KsCCVars.ClientLocale), text);
    }
}
