using System.Globalization;
using Content.Shared.Chat;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.RadioLocalization;

public sealed partial class KsRadioLocalization
{
    [Dependency] private IPrototypeManager _prototypeManager = default!;

    private readonly Dictionary<string, RadioMap> _cultures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RadioMap?> _resolvedCultures = new(StringComparer.OrdinalIgnoreCase);

    public void Initialize()
    {
        RebuildMaps();
        _prototypeManager.PrototypesReloaded += OnPrototypesReloaded;
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<KsRadioLocalePrototype>())
            RebuildMaps();
    }

    private void RebuildMaps()
    {
        _cultures.Clear();
        _resolvedCultures.Clear();
        foreach (var locale in _prototypeManager.EnumeratePrototypes<KsRadioLocalePrototype>())
        {
            var forward = new Dictionary<char, char>(locale.Keys);
            var reverse = new Dictionary<char, char>();
            foreach (var (canonical, localized) in forward)
                reverse.TryAdd(char.ToLowerInvariant(localized), canonical);
            _cultures.Add(CultureInfo.GetCultureInfo(locale.Culture).Name, new RadioMap(forward, reverse));
        }
    }

    private RadioMap? GetLocale(string? culture)
    {
        if (culture == null)
            return null;
        if (_resolvedCultures.TryGetValue(culture, out var cached))
            return cached;

        CultureInfo cultureInfo;
        try
        {
            cultureInfo = CultureInfo.GetCultureInfo(culture);
        }
        catch (ArgumentException)
        {
            _resolvedCultures[culture] = null;
            return null;
        }
        while (cultureInfo.Name.Length > 0)
        {
            if (_cultures.TryGetValue(cultureInfo.Name, out var locale))
            {
                _resolvedCultures[culture] = locale;
                return locale;
            }
            cultureInfo = cultureInfo.Parent;
        }
        _resolvedCultures[culture] = null;
        return null;
    }

    public char GetLocalizedKey(string? culture, char canonicalKey)
    {
        return GetLocale(culture)?.Forward.GetValueOrDefault(canonicalKey, canonicalKey) ?? canonicalKey;
    }

    public string NormalizePrefix(string? culture, string text)
    {
        if (text.Length < 2 || (text[0] != SharedChatSystem.RadioChannelPrefix
                              && text[0] != SharedChatSystem.RadioChannelAltPrefix))
            return text;

        var locale = GetLocale(culture);
        if (locale != null && locale.Reverse.TryGetValue(char.ToLowerInvariant(text[1]), out var canonicalKey))
            return $"{text[0]}{canonicalKey}{text[2..]}";
        return text;
    }

    private sealed record RadioMap(Dictionary<char, char> Forward, Dictionary<char, char> Reverse);
}
