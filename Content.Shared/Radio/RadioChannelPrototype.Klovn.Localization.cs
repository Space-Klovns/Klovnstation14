using Content.Shared._KS14.RadioLocalization;
using Robust.Shared.IoC;
using Robust.Shared.Localization;

namespace Content.Shared.Radio;

public sealed partial class RadioChannelPrototype
{
    public char LocalizedKeyCode => IoCManager.Resolve<KsRadioLocalization>().GetLocalizedKey(
        IoCManager.Resolve<ILocalizationManager>().DefaultCulture?.Name, KeyCode);

    public static string LocalizedDefaultChannelPrefix => $"{Content.Shared.Chat.SharedChatSystem.RadioChannelPrefix}{IoCManager.Resolve<KsRadioLocalization>().GetLocalizedKey(
        IoCManager.Resolve<ILocalizationManager>().DefaultCulture?.Name,
        Content.Shared.Chat.SharedChatSystem.DefaultChannelKey)}";
}
