using Content.Shared.Power;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.TeslaGate;

public abstract class SharedTeslaGateSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearanceSystem = default!;
    [Dependency] private readonly SharedPointLightSystem _pointLight = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<TeslaGateComponent, PowerChangedEvent>(OnPowerChange);
    }

    public bool IsFinishedShocking(TeslaGateComponent teslaGateComponent) => _gameTiming.CurTime > teslaGateComponent.LastShockTime + teslaGateComponent.ShockLength;

    protected void UpdateAppearance(Entity<TeslaGateComponent> teslaGate, bool active, TeslaGateVisualState state)
    {
        _appearanceSystem.SetData(teslaGate, TeslaGateVisuals.ShockingState, state);
        _pointLight.SetEnabled(teslaGate.Owner, active);

        Dirty(teslaGate);
    }
    public abstract void OnPowerChange(Entity<TeslaGateComponent> teslaGate, ref PowerChangedEvent args);
}
