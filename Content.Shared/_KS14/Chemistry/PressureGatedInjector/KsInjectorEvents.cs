using Content.Shared._KS14.PopupLocalization;
using Content.Shared.FixedPoint;

namespace Content.Shared._KS14.Chemistry.PressureGatedInjector;

/// <summary>
///     Raised on an injector about to be used on a mob, with the do-after time the injector has worked out, for whatever
///         makes it take longer. Handlers only write to the event; the injector shows <see cref="Message"/> to the user.
/// </summary>
[ByRefEvent]
public record struct KsModifyInjectorMobDelayEvent(EntityUid User, EntityUid Target, TimeSpan Delay)
{
    /// <summary>
    ///     Told to the user, saying why the injection takes longer.
    /// </summary>
    public KsPopupMessage? Message;
}

/// <summary>
///     Raised on an injector as it injects, with the amount it is about to put in, for how much of it actually gets in,
///         and whether what is left in the injector is lost.
/// </summary>
[ByRefEvent]
public record struct KsModifyInjectorAmountEvent(FixedPoint2 Amount, bool DiscardRest = false);
