using Content.Shared._KS14.TeslaGate;
using Content.Shared.Power;

namespace Content.Client._KS14.TeslaGate;

public sealed class TeslaGateSystem : SharedTeslaGateSystem
{
    public override void OnPowerChange(Entity<TeslaGateComponent> teslaGate, ref PowerChangedEvent args) => UpdateAppearance(teslaGate, args.Powered, teslaGate.Comp.CurrentlyShocking ? TeslaGateVisualState.Active : TeslaGateVisualState.Inactive);
}
