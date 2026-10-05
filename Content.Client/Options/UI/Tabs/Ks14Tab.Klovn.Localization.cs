using Content.Shared._KS14.CCVar;
using Content.Shared.Localizations;

namespace Content.Client.Options.UI.Tabs;

public sealed partial class Ks14Tab
{
    [Dependency] private ContentLocalizationManager _contentLocalizationManager = default!;

    private void AddClientLocaleOption()
    {
        // Native language names keep the selector usable whichever locale is active.
        var options = new List<OptionDropDownCVar<string>.ValueOption>();
        foreach (var culture in _contentLocalizationManager.GetAvailableCultures())
            options.Add(new OptionDropDownCVar<string>.ValueOption(culture.Name, culture.NativeName));
        Control.AddOptionDropDown(KsCCVars.ClientLocale, DropDownClientLocale, options);
    }
}
