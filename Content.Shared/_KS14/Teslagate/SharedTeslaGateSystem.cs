// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2025 github_actions[bot]
//
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.CompilerServices;
using Content.Shared.Power;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;
using DependencyAttribute = Robust.Shared.IoC.DependencyAttribute;


namespace Content.Shared._KS14.TeslaGate;

public abstract class SharedTeslaGateSystem : EntitySystem
{
    [Dependency] protected readonly SharedAudioSystem AudioSystem = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearanceSystem = default!;
    [Dependency] private readonly SharedPointLightSystem _pointLight = default!;
    [Dependency] private readonly SharedPowerReceiverSystem _powerReceiverSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<TeslaGateComponent, PowerChangedEvent>(OnPowerChange);
    }

    public bool IsFinishedShocking(TeslaGateComponent teslaGateComponent) => _gameTiming.CurTime > teslaGateComponent.LastShockTime + teslaGateComponent.ShockLength;

    protected void UpdateAppearance(Entity<TeslaGateComponent> teslaGate, bool active)
    {
        _appearanceSystem.SetData(teslaGate, TeslaGateVisuals.ShockingState, active ? TeslaGateVisualState.Active : TeslaGateVisualState.Inactive);
        _pointLight.SetEnabled(teslaGate.Owner, active);

        Dirty(teslaGate);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected bool CanStartWork(EntityUid uid)
    {
        if (!_powerReceiverSystem.IsPowered(uid))
            return false;

        return true;
    }

    public void Enable(Entity<TeslaGateComponent> teslaGate)
    {
        var (uid, teslaGateComponent) = teslaGate;

        if (CanStartWork(uid))
            AudioSystem.PlayPvs(teslaGateComponent.StartingSound, uid);

        ResetAccumulator(teslaGateComponent);
        teslaGateComponent.Enabled = true;

        Dirty(teslaGate);
    }

    public void Disable(Entity<TeslaGateComponent> teslaGate)
    {
        teslaGate.Comp.Enabled = false;
        ResetAccumulator(teslaGate);

        Dirty(teslaGate);
    }

    private void OnPowerChange(Entity<TeslaGateComponent> teslaGate, ref PowerChangedEvent args)
    {
        if (args.Powered)
        {
            if (teslaGate.Comp.WasDisabledByPower)
            {
                Enable(teslaGate);
                teslaGate.Comp.WasDisabledByPower = false;
            }
        }
        else
        {
            teslaGate.Comp.WasDisabledByPower |= teslaGate.Comp.Enabled;
            Disable(teslaGate);
        }
    }

    protected void ResetAccumulator(TeslaGateComponent teslaGateComponent)
    {
        teslaGateComponent.NextPulse = _gameTiming.CurTime + teslaGateComponent.PulseInterval;
        teslaGateComponent.LastShockTime = TimeSpan.MinValue;
    }
}
