// SPDX-FileCopyrightText: 2025 jhrushbe
//
// SPDX-License-Identifier: MPL-2.0

using Content.Shared.Containers.ItemSlots;
using Content.Shared.Emag.Systems;
using Content.Shared.IdentityManagement;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Shared._FarHorizons.Power.Generation.FissionGenerator;

public abstract class SharedNuclearReactorSystem : EntitySystem
{
    [Dependency] protected readonly SharedAudioSystem AudioSystem = default!;
    [Dependency] protected readonly SharedAppearanceSystem AppearanceSystem = default!;
    [Dependency] protected readonly IGameTiming GameTiming = default!;
    [Dependency] private readonly INetManager _netManager = default!;
    [Dependency] private readonly ItemSlotsSystem _slotsSystem = default!;
    [Dependency] private readonly SharedPopupSystem _popupSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NuclearReactorComponent, GotEmaggedEvent>(OnEmagged);

        // Bound UI subscriptions
        SubscribeLocalEvent<NuclearReactorComponent, ReactorEjectItemMessage>(OnEjectItemMessage);
        SubscribeLocalEvent<NuclearReactorComponent, ReactorSilenceAlarmsMessage>(OnSilenceAlarmsMessage);
    }

    protected bool ReactorTryGetSlot(EntityUid uid, string slotID, out ItemSlot? itemSlot) => _slotsSystem.TryGetSlot(uid, slotID, out itemSlot);

    public virtual void UpdateGridVisual(EntityUid uid, NuclearReactorComponent comp)
    {
        for (var x = 0; x < NuclearReactorComponent.ReactorGridWidth; x++)
        {
            for (var y = 0; y < NuclearReactorComponent.ReactorGridHeight; y++)
            {
                if (comp!.ComponentGrid[x, y] == null)
                {
                    AppearanceSystem.SetData(GetEntity(comp.VisualGrid[x, y]), ReactorCapVisuals.Sprite, ReactorCaps.Base);
                    continue;
                }
                else
                    AppearanceSystem.SetData(GetEntity(comp.VisualGrid[x, y]), ReactorCapVisuals.Sprite, ChoseSprite(comp.ComponentGrid[x, y]!.IconStateCap));
            }
        }
    }

    private static ReactorCaps ChoseSprite(string capName) => capName switch
    {
        "base_cap" => ReactorCaps.Base,

        "control_cap" => ReactorCaps.Control,
        "control_cap_melted_1" => ReactorCaps.ControlM1,
        "control_cap_melted_2" => ReactorCaps.ControlM2,
        "control_cap_melted_3" => ReactorCaps.ControlM3,
        "control_cap_melted_4" => ReactorCaps.ControlM4,

        "fuel_cap" => ReactorCaps.Fuel,
        "fuel_cap_melted_1" => ReactorCaps.FuelM1,
        "fuel_cap_melted_2" => ReactorCaps.FuelM2,
        "fuel_cap_melted_3" => ReactorCaps.FuelM3,
        "fuel_cap_melted_4" => ReactorCaps.FuelM4,

        "gas_cap" => ReactorCaps.Gas,
        "gas_cap_melted_1" => ReactorCaps.GasM1,
        "gas_cap_melted_2" => ReactorCaps.GasM2,
        "gas_cap_melted_3" => ReactorCaps.GasM3,
        "gas_cap_melted_4" => ReactorCaps.GasM4,

        "heat_cap" => ReactorCaps.Heat,
        "heat_cap_melted_1" => ReactorCaps.HeatM1,
        "heat_cap_melted_2" => ReactorCaps.HeatM2,
        "heat_cap_melted_3" => ReactorCaps.HeatM3,
        "heat_cap_melted_4" => ReactorCaps.HeatM4,

        _ => ReactorCaps.Base,
    };

    private void OnEjectItemMessage(EntityUid uid, NuclearReactorComponent component, ReactorEjectItemMessage args)
    {
        if (component.PartSlot.Item == null)
            return;

        _slotsSystem.TryEjectToHands(uid, component.PartSlot, args.Actor);
    }

    private void OnEmagged(Entity<NuclearReactorComponent> entity, ref GotEmaggedEvent args)
    {
        args.Handled = true;

        entity.Comp.NextIndicatorUpdateBy = GameTiming.CurTime + entity.Comp.EmagSabotageDelay;

        // if its smoking/burning, make it not smoking/burning.
        UpdateTempIndicators(entity, entity.Comp.isSmoking ? false : null, entity.Comp.isBurning ? false : null);
    }

    /// <summary>
    ///     Tries to delete something and changes the ref to it
    ///         accordingly. This was made to be used with properties
    ///         of <see cref="NuclearReactorComponent"/>.
    /// </summary>
    /// <returns>True if the entity already existed.</returns>
    protected bool TryQueueDelRef(ref NetEntity? netId)
    {
        var uid = GetEntity(netId);
        if (Deleted(uid))
        {
            netId = null;
            return false;
        }

        PredictedDel(uid);
        netId = null;

        return true;
    }

    private void OnSilenceAlarmsMessage(EntityUid uid, NuclearReactorComponent component, ref ReactorSilenceAlarmsMessage args)
    {
        // did we silence anything?
        var silencedAnything = false;

        if (GameTiming.CurTime >= component.NextIndicatorUpdateBy)
        {
            silencedAnything |= TryQueueDelRef(ref component.WarningAlertSoundUid);
            silencedAnything |= TryQueueDelRef(ref component.DangerAlertSoundUid);
        }

        if (!silencedAnything)
        {
            _popupSystem.PopupClient(Loc.GetString("reactor-alarms-silence-failed"), args.Actor);
            return;
        }

        // self message
        _popupSystem.PopupClient(
            Loc.GetString("reactor-alarms-silenced-message-self"),
            args.Actor,
            args.Actor,
            PopupType.MediumCaution
        );

        // others message
        _popupSystem.PopupEntity(
            Loc.GetString("reactor-alarms-silenced-message-others", ("user", Identity.Entity(args.Actor, EntityManager))),
            args.Actor,
            Filter.PvsExcept(args.Actor),
            true,
            PopupType.SmallCaution
        );

        AudioSystem.PlayPredicted(component.ManualSilenceSound, uid, args.Actor);
    }

    /// <summary>
    ///     Gets the changes in whether the reactor is considered smoking/burning.
    ///     Null values mean the bool stayed the same, with no change.
    /// </summary>
    protected static void GetOverallStateChange(NuclearReactorComponent component, out bool? isNowSmoking, out bool? isNowBurning)
    {
        if (component.Temperature >= component.ReactorOverheatTemp)
        {
            isNowSmoking = !component.isSmoking ? true : null;

            if (component.Temperature >= component.ReactorFireTemp)
                isNowBurning = !component.isBurning ? true : null;
            else
                isNowBurning = component.isBurning ? false : null;

            return;
        }

        isNowSmoking = component.isSmoking ? false : null;
        isNowBurning = component.isBurning ? false : null;
    }

    /// <param name="proper">Do radio messages?</param>
    protected void UpdateTempIndicators(Entity<NuclearReactorComponent> ent, bool? isNowSmoking, bool? isNowBurning, bool proper = true)
    {
        var comp = ent.Comp;
        var uid = ent.Owner;

        if (isNowSmoking == true)
        {
            comp.isSmoking = true;
            _popupSystem.PopupClient(Loc.GetString("reactor-smoke-start", ("owner", uid)), uid, uid, PopupType.MediumCaution);

            if (proper)
                SendEngiRadio(ent, Loc.GetString("reactor-smoke-start-message", ("owner", uid), ("temperature", Math.Round(comp.Temperature))));

            if (_netManager.IsServer) // unfortunately im too lazy to properly predict this
                ent.Comp.WarningAlertSoundUid ??= GetNetEntity(AudioSystem.PlayPvs(ent.Comp.WarningAlertSound, ent.Owner)?.Entity);
        }
        else if (isNowSmoking == false)
        {
            comp.isSmoking = false;
            _popupSystem.PopupClient(Loc.GetString("reactor-smoke-stop", ("owner", uid)), uid, uid, PopupType.Medium);

            if (proper)
                SendEngiRadio(ent, Loc.GetString("reactor-smoke-stop-message", ("owner", uid)));

            TryQueueDelRef(ref ent.Comp.WarningAlertSoundUid);
        }

        if (isNowBurning == true)
        {
            comp.isBurning = true;
            _popupSystem.PopupClient(Loc.GetString("reactor-fire-start", ("owner", uid)), uid, uid, PopupType.MediumCaution);

            if (proper)
                SendEngiRadio(ent, Loc.GetString("reactor-fire-start-message", ("owner", uid), ("temperature", Math.Round(comp.Temperature))));

            if (_netManager.IsServer) // unfortunately im too lazy to properly predict this
                ent.Comp.DangerAlertSoundUid ??= GetNetEntity(AudioSystem.PlayPvs(ent.Comp.DangerAlertSound, ent.Owner)?.Entity);
        }
        else if (isNowBurning == false)
        {
            comp.isBurning = false;
            _popupSystem.PopupClient(Loc.GetString("reactor-fire-stop", ("owner", uid)), uid, uid, PopupType.Medium);

            if (proper)
                SendEngiRadio(ent, Loc.GetString("reactor-fire-stop-message", ("owner", uid)));

            TryQueueDelRef(ref ent.Comp.DangerAlertSoundUid);
        }

        DirtyFields(ent, ent.Comp, null, nameof(ent.Comp.WarningAlertSoundUid), nameof(ent.Comp.DangerAlertSoundUid));

        if (_netManager.IsServer)
        {
            if (isNowSmoking != null)
                AppearanceSystem.SetData(uid, ReactorVisuals.Smoke, isNowSmoking);

            if (isNowBurning != null)
                AppearanceSystem.SetData(uid, ReactorVisuals.Fire, isNowBurning);
        }
    }

    protected virtual void SendEngiRadio(Entity<NuclearReactorComponent> ent, string message) { }
}

public static class NuclearReactorPrefabs
{
    private static readonly ReactorPartComponent c = BaseReactorComponents.ControlRod;
    private static readonly ReactorPartComponent f = BaseReactorComponents.FuelRod;
    private static readonly ReactorPartComponent g = BaseReactorComponents.GasChannel;
    private static readonly ReactorPartComponent h = BaseReactorComponents.HeatExchanger;

    public static readonly ReactorPartComponent?[,] Empty =
    {
        {
            null, null, null, null, null, null, null
        },
        {
            null, null, null, null, null, null, null
        },
        {
            null, null, null, null, null, null, null
        },
        {
            null, null, null, null, null, null, null
        },
        {
            null, null, null, null, null, null, null
        },
        {
            null, null, null, null, null, null, null
        },
        {
            null, null, null, null, null, null, null
        }
    };

    public static readonly ReactorPartComponent?[,] Normal =
    {
        {
            null, null, null, null, null, null, null
        },
        {
            null, null, null, null, null, null, null
        },
        {
            g, h, g, h, g, h, g
        },
        {
            h, null, c, null, c, null, h
        },
        {
            g, h, g, h, g, h, g
        },
        {
            null, null, null, null, null, null, null
        },
        {
            null, null, null, null, null, null, null
        }
    };

    public static readonly ReactorPartComponent?[,] Debug =
    {
        {
            null, null, null, null, null, null, null
        },
        {
            null, null, null, null, null, null, null
        },
        {
            g, h, g, h, g, h, g
        },
        {
            h, f, c, f, c, f, h
        },
        {
            g, h, g, h, g, h, g
        },
        {
            null, null, null, null, null, null, null
        },
        {
            null, null, null, null, null, null, null
        }
    };

    public static readonly ReactorPartComponent?[,] Meltdown =
    {
        {
            f, f, f, f, f, f, f
        },
        {
            f, f, f, f, f, f, f
        },
        {
            f, f, f, f, f, f, f
        },
        {
            f, f, f, f, f, f, f
        },
        {
            f, f, f, f, f, f, f
        },
        {
            f, f, f, f, f, f, f
        },
        {
            f, f, f, f, f, f, f
        },
    };

    public static readonly ReactorPartComponent?[,] Alignment =
    {
        {
            null, null, null, null, null, null, c
        },
        {
            null, null, null, null, null, c, null
        },
        {
            null, null, null, null, c, null, null
        },
        {
            null, null, null, c, null, null, null
        },
        {
            null, null, c, null, c, null, null
        },
        {
            null, c, null, null, null, c, null
        },
        {
            c, null, null, null, null, null, c
        }
    };
}
