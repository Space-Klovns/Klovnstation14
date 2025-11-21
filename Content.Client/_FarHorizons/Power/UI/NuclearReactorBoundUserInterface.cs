// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2025 github_actions[bot]
// SPDX-FileCopyrightText: 2025 jhrushbe
// SPDX-FileCopyrightText: 2025 rottenheadphones
//
// SPDX-License-Identifier: MPL-2.0

using Content.Shared._FarHorizons.Power.Generation.FissionGenerator;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._FarHorizons.Power.UI;

/// <summary>
/// Initializes a <see cref="NuclearReactorWindow"/> and updates it when new server messages are received.
/// </summary>
[UsedImplicitly]
public sealed class NuclearReactorBoundUserInterface : BoundUserInterface
{

    [ViewVariables]
    private NuclearReactorWindow? _window;

    public NuclearReactorBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<NuclearReactorWindow>();

        _window.ItemActionButtonPressed += OnActionButtonPressed;
        _window.EjectButtonPressed += OnEjectButtonPressed;
        _window.SilenceButtonPressed += OnSilenceButtonPressed;
        _window.ControlRodModify += OnControlRodModify;

        Update();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is not NuclearReactorBuiState reactorState)
            return;

        _window?.Update(reactorState);
    }

    private void OnActionButtonPressed(Vector2d vector)
    {
        if (_window is null) return;

        SendPredictedMessage(new ReactorItemActionMessage(vector));
    }

    private void OnEjectButtonPressed()
    {
        if (_window is null) return;

        SendPredictedMessage(new ReactorEjectItemMessage());
    }

    private void OnSilenceButtonPressed()
    {
        if (_window is null) return;

        SendPredictedMessage(new ReactorSilenceAlarmsMessage());
    }

    private void OnControlRodModify(float amount)
    {
        if (_window is null) return;

        SendPredictedMessage(new ReactorControlRodModifyMessage(amount));
    }
}
