// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2025 github_actions[bot]
//
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics.CodeAnalysis;
using Content.Server.Wires;
using Content.Shared._KS14.TeslaGate;
using Content.Shared.Wires;

namespace Content.Server._KS14.TeslaGate;

public sealed partial class TeslaGateSafetyWireAction : ComponentWireAction<TeslaGateComponent>
{
    public override string Name { get; set; } = "wire-name-teslagate-safety";

    public override Color Color { get; set; } = Color.LightYellow;

    public override object? StatusKey { get; } = TeslaGateSafetyWireKey.StatusKey;


    [DataField("timeout")]
    private int _timeout = 15;

    private void SetSafety(EntityUid owner, TeslaGateComponent teslaGateComponent, bool setting)
    {
        var isHacked = !setting;
        teslaGateComponent.IsIntervalHacked = isHacked;

        if (isHacked)
            teslaGateComponent.PulseInterval = teslaGateComponent.HackedPulseInterval;
        else
            teslaGateComponent.PulseInterval = teslaGateComponent.DefaultPulseInterval;

        teslaGateComponent.IsTimerWireCut = setting;
    }

    public override bool Cut(EntityUid user, Wire wire, TeslaGateComponent teslaGateComponent)
    {
        var owner = wire.Owner;

        SetSafety(owner, teslaGateComponent, false);
        WiresSystem.TryCancelWireAction(owner, TeslaGateSafetyWireKey.StatusKey);
        teslaGateComponent.IsTimerWireCut = true;

        return true;
    }

    public override bool Mend(EntityUid user, Wire wire, TeslaGateComponent teslaGateComponent)
    {
        SetSafety(wire.Owner, teslaGateComponent, true);
        teslaGateComponent.IsTimerWireCut = false;

        return true;
    }

    public override void Pulse(EntityUid user, Wire wire, TeslaGateComponent teslaGateComponent)
    {
        var owner = wire.Owner;

        SetSafety(owner, teslaGateComponent, false);
        WiresSystem.StartWireAction(owner, _timeout, TeslaGateSafetyWireKey.StatusKey, new TimedWireEvent(AwaitSafetyTimerFinish, wire));
    }

    public override void Update(Wire wire)
    {
        if (!IsPowered(wire.Owner))
            WiresSystem.TryCancelWireAction(wire.Owner, TeslaGateSafetyWireKey.StatusKey);
    }

    private void AwaitSafetyTimerFinish(Wire wire)
    {
        var owner = wire.Owner;
        if (!EntityManager.TryGetComponent<TeslaGateComponent>(owner, out var teslaGateComponent))
            return;

        if (!wire.IsCut)
            SetSafety(owner, teslaGateComponent, true);
    }

    public override StatusLightState? GetLightState(Wire wire, TeslaGateComponent teslaGateComponent)
    {
        if (!teslaGateComponent.Enabled)
            return StatusLightState.Off;

        return teslaGateComponent.IsIntervalHacked
            ? StatusLightState.BlinkingFast
            : StatusLightState.BlinkingSlow;
    }
}

public sealed partial class TeslaGateForceWireAction : ComponentWireAction<TeslaGateComponent>
{
    private TeslaGateSystem _teslaGateSystem = default!;

    public override string Name { get; set; } = "wire-name-teslagate-alertsensor";

    public override Color Color { get; set; } = Color.Navy;

    public override object? StatusKey { get; } = TeslaGateForceWireKey.StatusKey;

    public override void Initialize()
    {
        base.Initialize();

        _teslaGateSystem = EntityManager.System<TeslaGateSystem>();
    }

    private bool TryGetGateEnt(Wire wire, [NotNullWhen(true)] out Entity<TeslaGateComponent>? teslaGate)
    {
        var owner = wire.Owner;
        if (!EntityManager.TryGetComponent<TeslaGateComponent>(owner, out var teslaGateComponent))
        {
            teslaGate = null;
            return false;
        }

        teslaGate = (owner, teslaGateComponent);
        return true;
    }

    public override bool Cut(EntityUid user, Wire wire, TeslaGateComponent teslaGateComponent)
    {
        teslaGateComponent.IsForceHacked = true;
        return true;
    }

    public override bool Mend(EntityUid user, Wire wire, TeslaGateComponent teslaGateComponent)
    {
        teslaGateComponent.IsForceHacked = false;
        return true;
    }

    public override void Pulse(EntityUid user, Wire wire, TeslaGateComponent teslaGateComponent)
    {
        if (wire.IsCut)
            return;

        // if it was enabled & hacked, turn it off
        teslaGateComponent.IsForceHacked = !teslaGateComponent.IsForceHacked;
        if (!teslaGateComponent.IsForceHacked && teslaGateComponent.Enabled)
            _teslaGateSystem.Disable((wire.Owner, teslaGateComponent));
    }

    public override StatusLightState? GetLightState(Wire wire, TeslaGateComponent teslaGateComponent)
    {
        return teslaGateComponent.IsForceHacked
            ? StatusLightState.Off
            : StatusLightState.On;
    }
}

public sealed partial class TeslaGateAuxWireAction : ComponentWireAction<TeslaGateComponent>
{
    private TeslaGateSystem _teslaGateSystem = default!;

    public override string Name { get; set; } = "wire-name-teslagate-aux-current";

    public override Color Color { get; set; } = Color.Maroon;

    public override object? StatusKey { get; } = TeslaGateAuxWireKey.StatusKey;

    public override void Initialize()
    {
        base.Initialize();

        _teslaGateSystem = EntityManager.System<TeslaGateSystem>();
    }

    public override bool Cut(EntityUid user, Wire wire, TeslaGateComponent teslaGateComponent)
    {
        var owner = wire.Owner;

        teslaGateComponent.IsAuxWireCut = true;
        if (teslaGateComponent.Enabled && teslaGateComponent.IsForceHacked)
            _teslaGateSystem.Disable((owner, teslaGateComponent));

        return true;
    }

    public override bool Mend(EntityUid user, Wire wire, TeslaGateComponent teslaGateComponent)
    {
        var owner = wire.Owner;

        teslaGateComponent.IsAuxWireCut = false;
        if (!teslaGateComponent.Enabled && teslaGateComponent.IsForceHacked)
            _teslaGateSystem.Enable((owner, teslaGateComponent));

        return true;
    }

    public override void Pulse(EntityUid user, Wire wire, TeslaGateComponent teslaGateComponent)
    {
        if (!teslaGateComponent.Enabled && teslaGateComponent.IsForceHacked)
            _teslaGateSystem.Enable((wire.Owner, teslaGateComponent));
    }

    public override StatusLightState? GetLightState(Wire wire, TeslaGateComponent teslaGateComponent)
    {
        return teslaGateComponent.Enabled
            ? StatusLightState.On
            : StatusLightState.Off;
    }
}
