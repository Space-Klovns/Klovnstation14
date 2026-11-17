using Content.Shared._KS14.PopupLocalization;

namespace Content.Shared._KS14.Chemistry.PressureGatedInjector;

/// <summary>
///     Slows a pressure-gated injector down, and wastes half its dose, wherever the air is too thick for it.
/// </summary>
public sealed partial class KsPressureGatedInjectorSystem : EntitySystem
{
    [SubscribeLocalEvent]
    private void OnModifyMobDelay(Entity<KsPressureGatedInjectorComponent> injector, ref KsModifyInjectorMobDelayEvent args)
    {
        if (injector.Comp.LowPressure)
            return;

        args.Delay += injector.Comp.HighPressureDelay;
        args.Message = KsPopupMessage.Create("ks-pressure-gated-injector-high-pressure");
    }

    [SubscribeLocalEvent]
    private void OnModifyAmount(Entity<KsPressureGatedInjectorComponent> injector, ref KsModifyInjectorAmountEvent args)
    {
        if (injector.Comp.LowPressure)
            return;

        args.Amount *= injector.Comp.HighPressureAmount;
        args.DiscardRest = true;
    }
}
