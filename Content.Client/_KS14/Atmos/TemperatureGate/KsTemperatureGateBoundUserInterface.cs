using Content.Shared._KS14.Atmos.TemperatureGate;
using Content.Shared.IdentityManagement;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._KS14.Atmos.TemperatureGate;

[UsedImplicitly]
public sealed partial class KsTemperatureGateBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    [ViewVariables]
    private KsTemperatureGateWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<KsTemperatureGateWindow>();
        _window.ToggleStatusButtonPressed += enabled => SendPredictedMessage(new KsTemperatureGateToggleStatusMessage(enabled));
        _window.ModeChanged += mode => SendPredictedMessage(new KsTemperatureGateChangeModeMessage(mode));
        _window.RangeSet += (minimum, maximum) => SendPredictedMessage(new KsTemperatureGateChangeRangeMessage(minimum, maximum));
        _window.TargetAndToleranceSet += (target, tolerance) =>
        {
            SendPredictedMessage(new KsTemperatureGateChangeTargetMessage(target));
            SendPredictedMessage(new KsTemperatureGateChangeToleranceMessage(tolerance));
        };
        Update();
    }

    public override void Update()
    {
        if (_window == null || !EntMan.TryGetComponent(Owner, out KsTemperatureGateComponent? gate))
            return;

        _window.Title = Identity.Name(Owner, EntMan);
        _window.UpdateState(gate);
    }
}
