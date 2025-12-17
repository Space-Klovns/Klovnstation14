// SPDX-FileCopyrightText: 2025 github_actions[bot]
// SPDX-FileCopyrightText: 2025 nabegator220
//
// SPDX-License-Identifier: MPL-2.0

using Content.Shared.Actions;
using Content.Shared._KS14.Silicons.Bots.Components;
using Robust.Shared.Serialization;
using Content.Shared.Popups;
using Robust.Shared.Map;

namespace Content.Shared.Silicons.StationAi;

public abstract partial class SharedStationAiSystem
{
    private EntityUid currentTargetedBot;

    private void InitializeBot()
    {
        //subscribe the 2 ai actions you need
        SubscribeLocalEvent<StationAiHeldComponent, SelectControlledBotEvent>(OnSelectBot);
        SubscribeLocalEvent<StationAiHeldComponent, MoveControlledBotToPositionEvent>(OnMoveBot);
    }
    //when you want to select a bot to wrangle
    private void OnSelectBot(EntityUid ent, StationAiHeldComponent component, SelectControlledBotEvent args)
    {
        if (args.Handled)
            return;
        args.Handled = true;
        var target = args.Target;

        if (!HasComp<ControllableBotComponent>(target))
        {
            _popup.PopupClient(Loc.GetString("ai-entity-not-controllable"), args.Performer, PopupType.MediumCaution);
            return;
        }

        currentTargetedBot = target;
        _popup.PopupClient(Loc.GetString("ai-bot-selection-successful"), args.Performer, PopupType.Medium);
    }
    //when you want to move selected bot
    private void OnMoveBot(EntityUid ent, StationAiHeldComponent component, MoveControlledBotToPositionEvent args)
    {
        var target = args.Target;
        //do we have an actual bot to move? did it not get deleted in between it being selected and moved?
        if (currentTargetedBot == null || (currentTargetedBot != null && !Exists(currentTargetedBot)))
        {
            _popup.PopupClient(Loc.GetString("ai-controlled-bot-not-found"), args.Performer, PopupType.MediumCaution);
            return;
        }
        //move to server, our job here is done
        _popup.PopupClient(Loc.GetString("ai-bot-targeting-successful"), args.Performer, PopupType.Medium);
        TryMoveBot(currentTargetedBot, target);
    }
    //server glue
    public virtual void TryMoveBot(
        EntityUid botUid,
        EntityCoordinates targetCoordinates)
    {}
}
/// <summary>
/// Invoked when the entity target action ActionSelectControlledBot is called.
/// </summary>
public sealed partial class SelectControlledBotEvent : EntityTargetActionEvent;

/// <summary>
/// Invoked when the entity target action ActionMoveControlledBot is called.
/// </summary>
public sealed partial class MoveControlledBotToPositionEvent : WorldTargetActionEvent;
