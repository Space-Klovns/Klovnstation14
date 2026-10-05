using Content.Shared._KS14.CCVar;

namespace Content.Client.Options.UI.Tabs;

public sealed partial class Ks14Tab
{
    private void AddClientLocaleOption()
    {
        // Native language names keep the selector usable whichever locale is active.
        Control.AddOptionDropDown(KsCCVars.ClientLocale, DropDownClientLocale,
        [
            new OptionDropDownCVar<string>.ValueOption("en-US", "English"),
            new OptionDropDownCVar<string>.ValueOption("ru-RU", "Русский"),
        ]);
    }
}
