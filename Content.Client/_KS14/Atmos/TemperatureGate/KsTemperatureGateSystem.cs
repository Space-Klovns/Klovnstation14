using Content.Shared._KS14.Atmos.TemperatureGate;

namespace Content.Client._KS14.Atmos.TemperatureGate;

public sealed partial class KsTemperatureGateSystem : KsSharedTemperatureGateSystem
{
    [SubscribeLocalEvent]
    private void OnAfterState(Entity<KsTemperatureGateComponent> entity, ref AfterAutoHandleStateEvent args)
    {
        UpdateUi(entity);
    }

    protected override void UpdateUi(Entity<KsTemperatureGateComponent> entity)
    {
        if (UserInterfaceSystem.TryGetOpenUi(entity.Owner, KsTemperatureGateUiKey.Key, out var boundUserInterface))
            boundUserInterface.Update();
    }
}
