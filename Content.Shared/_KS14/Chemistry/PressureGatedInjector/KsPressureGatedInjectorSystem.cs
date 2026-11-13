using Content.Shared._KS14.PopupLocalization;
using Content.Shared.Popups;

namespace Content.Shared._KS14.Chemistry.PressureGatedInjector;

public sealed partial class KsPressureGatedInjectorSystem : EntitySystem
{
    [Dependency] private SharedPopupSystem _popupSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<KsPressureGatedInjectorComponent, KsGetInjectorMobDelayEvent>(OnGetMobDelay);
        SubscribeLocalEvent<KsPressureGatedInjectorComponent, KsGetInjectorAmountEvent>(OnGetAmount);
    }

    private void OnGetMobDelay(Entity<KsPressureGatedInjectorComponent> injector, ref KsGetInjectorMobDelayEvent args)
    {
        if (injector.Comp.LowPressure)
            return;

        args.Delay += injector.Comp.HighPressureDelay;
        _popupSystem.PopupClient(KsPopupMessage.Create("ks-pressure-gated-injector-high-pressure"), args.User, args.User);
    }

    private void OnGetAmount(Entity<KsPressureGatedInjectorComponent> injector, ref KsGetInjectorAmountEvent args)
    {
        if (injector.Comp.LowPressure)
            return;

        args.Amount *= injector.Comp.HighPressureAmount;
        args.DiscardRest = true;
    }
}
