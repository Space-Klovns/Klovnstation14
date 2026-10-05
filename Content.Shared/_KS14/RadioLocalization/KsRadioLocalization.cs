using System.Linq;
using Content.Shared.Chat;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.RadioLocalization;

public static class KsRadioLocalization
{
    private static KsRadioLocalePrototype? GetLocale(IPrototypeManager prototypeManager, string? culture)
    {
        if (culture == null)
            return null;

        var locales = prototypeManager.EnumeratePrototypes<KsRadioLocalePrototype>();
        return locales.FirstOrDefault(locale => locale.Culture.Equals(culture, StringComparison.OrdinalIgnoreCase))
            ?? locales.FirstOrDefault(locale => locale.Culture.Equals(culture.Split('-')[0], StringComparison.OrdinalIgnoreCase));
    }

    public static char GetLocalizedKey(IPrototypeManager prototypeManager, string? culture, char canonicalKey)
    {
        var localePrototype = GetLocale(prototypeManager, culture);
        return localePrototype?.Keys.GetValueOrDefault(canonicalKey, canonicalKey) ?? canonicalKey;
    }

    public static string NormalizePrefix(IPrototypeManager prototypeManager, string? culture, string text)
    {
        if (text.Length < 2 || (text[0] != SharedChatSystem.RadioChannelPrefix
                              && text[0] != SharedChatSystem.RadioChannelAltPrefix))
            return text;

        var localePrototype = GetLocale(prototypeManager, culture);
        if (localePrototype == null)
            return text;

        var localizedKey = char.ToLowerInvariant(text[1]);
        foreach (var (canonicalKey, mappedKey) in localePrototype.Keys)
        {
            if (mappedKey == localizedKey)
                return $"{text[0]}{canonicalKey}{text[2..]}";
        }

        return text;
    }
}
