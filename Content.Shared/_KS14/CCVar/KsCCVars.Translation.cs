using Content.Shared.Administration;
using Content.Shared.CCVar.CVarAccess;
using Robust.Shared.Configuration;

namespace Content.Shared._KS14.CCVar;

public sealed partial class KsCCVars
{
    /// <summary>
    ///     Master switch for KS14 DeepL chat translation. Defaults OFF.
    /// </summary>
    [CVarControl(AdminFlags.Server)]
    public static readonly CVarDef<bool> TranslateEnabled =
        CVarDef.Create("klovn.translate.enabled", false, CVar.ARCHIVE | CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     DeepL API key(s). Accepts a COMMA-SEPARATED list, one key per DeepL account; translation uses one
    ///     account until its budget/quota is spent, then rotates to the next. Free-tier keys end with ":fx"
    ///     (the SDK routes Free vs Pro from that suffix). CONFIDENTIAL so keys are never revealed to clients
    ///     or written to logs. NEVER commit a real key here: set it at runtime via the server console
    ///     (<c>cvar klovn.translate.deepl_key ...</c>) or the untracked server_config.toml [klovn.translate].
    /// </summary>
    [CVarControl(AdminFlags.Host)]
    public static readonly CVarDef<string> TranslateDeeplKey =
        CVarDef.Create("klovn.translate.deepl_key", "", CVar.SERVERONLY | CVar.CONFIDENTIAL);

    /// <summary>
    ///     Per-player language, used as BOTH the source (what they type) and the target (what they read).
    ///     A DeepL target code, e.g. "EN-US", "DE", "RU". Empty means translation is off for that player.
    ///     Replicated so the server can read each client's choice via GetClientCVar.
    /// </summary>
    public static readonly CVarDef<string> TranslateLanguage =
        CVarDef.Create("klovn.translate.language", "", CVar.CLIENT | CVar.REPLICATED | CVar.ARCHIVE);

    /// <summary>
    ///     Per-key character budget. Once a single DeepL account (key) has spent this many characters,
    ///     translation rotates to the next configured key; once every key is spent, translation stops. Set it
    ///     just under the account's real DeepL limit so rotation happens BEFORE DeepL hard-stops: the free
    ///     tier is a one-time 1,000,000 character credit, so 950000 leaves a safety margin. 0 disables the
    ///     local cap (rotation then relies solely on DeepL's own quota response).
    /// </summary>
    public static readonly CVarDef<int> TranslateMonthlyBudget =
        CVarDef.Create("klovn.translate.monthly_budget", 950000, CVar.SERVERONLY);

    /// <summary>
    ///     Baseline per-speaker cooldown, in seconds, before another of their messages triggers a call.
    /// </summary>
    public static readonly CVarDef<float> TranslateCooldownBaseline =
        CVarDef.Create("klovn.translate.cooldown_baseline", 0.5f, CVar.SERVERONLY);

    /// <summary>
    ///     Extra per-character cooldown, in seconds, added on top of the baseline.
    /// </summary>
    public static readonly CVarDef<float> TranslateCooldownPerChar =
        CVarDef.Create("klovn.translate.cooldown_per_char", 0.02f, CVar.SERVERONLY);

    /// <summary>
    ///     Messages shorter than this are not translated (nothing meaningful to translate).
    /// </summary>
    public static readonly CVarDef<int> TranslateMinLength =
        CVarDef.Create("klovn.translate.min_length", 2, CVar.SERVERONLY);

    /// <summary>
    ///     Messages longer than this are not translated (cost control).
    /// </summary>
    public static readonly CVarDef<int> TranslateMaxLength =
        CVarDef.Create("klovn.translate.max_length", 512, CVar.SERVERONLY);

    /// <summary>
    ///     How many recent plain lines of a channel are fed to DeepL through its unbilled "context" parameter
    ///     to bias translation of terse chat toward the ongoing conversation. 0 disables the rolling buffer
    ///     (the static setting hint is still sent).
    /// </summary>
    public static readonly CVarDef<int> TranslateContextLines =
        CVarDef.Create("klovn.translate.context_lines", 6, CVar.SERVERONLY);

    /// <summary>
    ///     How often, in minutes, to poll DeepL for authoritative billing-period usage. When usage reports any
    ///     limit reached, translation is disabled for the rest of the period. 0 disables polling (the local
    ///     character counter is then the only budget guard).
    /// </summary>
    public static readonly CVarDef<float> TranslateUsagePollMinutes =
        CVarDef.Create("klovn.translate.usage_poll_minutes", 5f, CVar.SERVERONLY);
}
