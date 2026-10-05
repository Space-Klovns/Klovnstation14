using Content.Shared._KS14.RadioLocalization;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.Shared.Radio;

public sealed partial class RadioChannelPrototype
{
    public char LocalizedKeyCode => KsRadioLocalization.GetLocalizedKey(
        IoCManager.Resolve<IPrototypeManager>(), IoCManager.Resolve<ILocalizationManager>().DefaultCulture?.Name, KeyCode);

    public static string LocalizedDefaultChannelPrefix => $"{Content.Shared.Chat.SharedChatSystem.RadioChannelPrefix}{KsRadioLocalization.GetLocalizedKey(
        IoCManager.Resolve<IPrototypeManager>(), IoCManager.Resolve<ILocalizationManager>().DefaultCulture?.Name,
        Content.Shared.Chat.SharedChatSystem.DefaultChannelKey)}";
}
