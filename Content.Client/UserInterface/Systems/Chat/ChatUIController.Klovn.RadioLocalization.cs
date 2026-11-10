using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.RadioLocalization;

namespace Content.Client.UserInterface.Systems.Chat;

public sealed partial class ChatUIController
{
    [Dependency] private KsRadioLocalization _radioLocalization = default!;

    private string NormalizeLocalizedRadioPrefix(string text)
    {
        return _radioLocalization.NormalizePrefix(_config.GetCVar(KsCCVars.ClientLocale), text);
    }
}
