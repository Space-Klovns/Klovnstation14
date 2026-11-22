using Robust.Shared.Configuration;

namespace Content.Shared._KS14.CCVar;

public sealed partial class KsCCVars
{
    /// <summary>
    ///     Whether mobs with eyelids blink on their own every few seconds while their eyes are open. When false, eyes
    ///         only close for a reason: sleep, blindness (flashes, blindfolds), the close-eyes action, or a deliberate
    ///         blink emote. Dead eyes stay open either way.
    /// </summary>
    public static readonly CVarDef<bool> BlinkingPeriodic =
        CVarDef.Create("klovn.blinking.periodic", true, CVar.SERVER | CVar.REPLICATED);
}
