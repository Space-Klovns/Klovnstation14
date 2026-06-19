using Content.Client.UserInterface;
using Content.Shared._FarHorizons.Power.Generation.FissionGenerator;
using JetBrains.Annotations;
using Robust.Client.Timing;
using Robust.Client.UserInterface;

namespace Content.Client._FarHorizons.Power.UI;

/// <summary>
/// Initializes a <see cref="TurbineWindow"/> and updates it when new server messages are received.
/// </summary>
[UsedImplicitly]
public sealed class TurbineBoundUserInterface : BoundUserInterface
{
    [Dependency] private readonly IClientGameTiming _gameTiming = null!;

    [ViewVariables]
    private TurbineWindow? _window;

    private BuiPredictionState? _pred;

    public TurbineBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
        IoCManager.InjectDependencies(this);
    }

    protected override void Open()
    {
        base.Open();

        _pred = new BuiPredictionState(this, _gameTiming);

        _window = this.CreateWindow<TurbineWindow>();
        _window.SetEntity(Owner);

        _window.TurbineFlowRateChanged += val =>
        {
            _pred.SendMessage(new TurbineChangeFlowRateMessage(val));
            _window.SetFlowRateInput(val); // Optimistically update input
        };

        _window.TurbineStatorLoadChanged += val =>
        {
            _pred.SendMessage(new TurbineChangeStatorLoadMessage(val));
            _window.SetStatorLoadInput(val); // Optimistically update input
        };

        if (EntMan.TryGetComponent(Owner, out TurbineComponent? comp))
        {
            _window.SetFlowRateInput(comp.FlowRate);
            _window.SetStatorLoadInput(comp.StatorLoad);
        }

        Update();
    }

    public override void Update()
    {
        base.Update();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is not TurbineBuiState turbineState)
            return;

        foreach (var replayMsg in _pred!.MessagesToReplay())
        {
            switch (replayMsg)
            {
                case TurbineChangeFlowRateMessage setFlowRate:
                    turbineState.FlowRate = setFlowRate.FlowRate;
                    break;

                case TurbineChangeStatorLoadMessage setStatorLoad:
                    turbineState.StatorLoad = setStatorLoad.StatorLoad;
                    break;
            }
        }

        _window?.Update(turbineState);
    }
}
