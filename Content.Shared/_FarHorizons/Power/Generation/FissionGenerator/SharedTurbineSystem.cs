// SPDX-FileCopyrightText: 2025 jhrushbe
//
// SPDX-License-Identifier: MPL-2.0

using Content.Shared.Administration.Logs;
using Content.Shared.Damage;
using Content.Shared.Database;
using Content.Shared.Examine;

namespace Content.Shared._FarHorizons.Power.Generation.FissionGenerator;

public abstract class SharedTurbineSystem : EntitySystem
{
    [Dependency] protected readonly DamageableSystem DamageableSystem = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly ISharedAdminLogManager _adminLogger = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TurbineComponent, ExaminedEvent>(OnExamined);

        SubscribeLocalEvent<TurbineComponent, TurbineChangeFlowRateMessage>(OnTurbineFlowRateChanged);
        SubscribeLocalEvent<TurbineComponent, TurbineChangeStatorLoadMessage>(OnTurbineStatorLoadChanged);
    }

    private void OnExamined(Entity<TurbineComponent> ent, ref ExaminedEvent args)
    {
        var comp = ent.Comp;
        if (!Transform(ent).Anchored || !args.IsInDetailsRange) // Not anchored? Out of range? No status.
            return;

        using (args.PushGroup(nameof(TurbineComponent)))
        {
            if (!comp.Ruined)
            {
                switch (comp.RPM)
                {
                    case float n when n is >= 0 and <= 1:
                        args.PushMarkup(Loc.GetString("turbine-spinning-0")); // " The blades are not spinning."
                        break;
                    case float n when n is > 1 and <= 60:
                        args.PushMarkup(Loc.GetString("turbine-spinning-1")); // " The blades are turning slowly."
                        break;
                    case float n when n > 60 && n <= comp.BestRPM * 0.5:
                        args.PushMarkup(Loc.GetString("turbine-spinning-2")); // " The blades are spinning."
                        break;
                    case float n when n > comp.BestRPM * 0.5 && n <= comp.BestRPM * 1.2:
                        args.PushMarkup(Loc.GetString("turbine-spinning-3")); // " The blades are spinning quickly."
                        break;
                    case float n when n > comp.BestRPM * 1.2 && n <= float.PositiveInfinity:
                        args.PushMarkup(Loc.GetString("turbine-spinning-4")); // " The blades are spinning out of control!"
                        break;
                    default:
                        break;
                }
            }
        }
    }

    protected void UpdateAppearance(EntityUid uid, TurbineComponent? comp = null, AppearanceComponent? appearance = null)
    {
        if (!Resolve(uid, ref comp, ref appearance, false))
            return;

        _appearance.TryGetData<bool>(uid, TurbineVisuals.TurbineRuined, out var IsSpriteRuined);
        if (comp.Ruined)
        {
            if (!IsSpriteRuined)
            {
                _appearance.SetData(uid, TurbineVisuals.TurbineRuined, true);
            }
        }
        else
        {
            if (IsSpriteRuined)
            {
                _appearance.SetData(uid, TurbineVisuals.TurbineRuined, false);
            }
            switch (comp.RPM)
            {
                case float n when n is > 1 and <= 60:
                    _appearance.SetData(uid, TurbineVisuals.TurbineSpeed, TurbineSpeed.SpeedSlow);
                    break;
                case float n when n > 60 && n <= comp.BestRPM * 0.5:
                    _appearance.SetData(uid, TurbineVisuals.TurbineSpeed, TurbineSpeed.SpeedMid);
                    break;
                case float n when n > comp.BestRPM * 0.5 && n <= comp.BestRPM * 1.2:
                    _appearance.SetData(uid, TurbineVisuals.TurbineSpeed, TurbineSpeed.SpeedFast);
                    break;
                case float n when n > comp.BestRPM * 1.2 && n <= float.PositiveInfinity:
                    _appearance.SetData(uid, TurbineVisuals.TurbineSpeed, TurbineSpeed.SpeedOverspeed);
                    break;
                default:
                    _appearance.SetData(uid, TurbineVisuals.TurbineSpeed, TurbineSpeed.SpeedStill);
                    break;
            }
        }

        _appearance.SetData(uid, TurbineVisuals.DamageSpark, comp.IsSparking);
        _appearance.SetData(uid, TurbineVisuals.DamageSmoke, comp.IsSmoking);
    }

    protected void UpdateRpm(EntityUid uid, TurbineComponent component, float newRpm)
    {
        component.RPM = newRpm;
        DirtyField(uid, component, nameof(component.RPM));
    }

    #region User Interface
    private void OnTurbineFlowRateChanged(EntityUid uid, TurbineComponent turbine, TurbineChangeFlowRateMessage args)
    {
        turbine.FlowRate = Math.Clamp(args.FlowRate, 0f, turbine.FlowRateMax);
        Dirty(uid, turbine);
        UpdateUi((uid, turbine));
        _adminLogger.Add(LogType.AtmosVolumeChanged, LogImpact.Medium,
            $"{ToPrettyString(args.Actor):player} set the transfer rate on {ToPrettyString(uid):device} to {args.FlowRate}");
    }

    private void OnTurbineStatorLoadChanged(EntityUid uid, TurbineComponent turbine, TurbineChangeStatorLoadMessage args)
    {
        turbine.StatorLoad = Math.Clamp(args.StatorLoad, 1000f, 500000f);
        Dirty(uid, turbine);
        UpdateUi((uid, turbine));
        _adminLogger.Add(LogType.AtmosVolumeChanged, LogImpact.Medium,
            $"{ToPrettyString(args.Actor):player} set the transfer rate on {ToPrettyString(uid):device} to {args.StatorLoad}");
    }

    protected virtual void UpdateUi(Entity<TurbineComponent> entity) { }
    #endregion
}
