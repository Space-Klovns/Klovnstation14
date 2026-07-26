using System.Linq;
using Content.Shared.Actions;
using Content.Shared.Popups;
using Content.Shared.Whitelist;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.Silicons.Bots;

public abstract partial class SharedBotWranglerSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<ActivelyWrangledBotComponent, ComponentShutdown>(OnActivelyWrangledBotShutdown);
        SubscribeLocalEvent<BotWranglerComponent, ComponentShutdown>(OnBotWranglerShutdown);

        //subscribe the 2 ai actions you need
        SubscribeLocalEvent<BotWranglerComponent, SelectControlledBotEvent>(OnSelectControlledBot);
        SubscribeLocalEvent<BotWranglerComponent, MoveControlledBotToPositionEvent>(OnMoveControlledBotToPosition);
    }

    private void OnActivelyWrangledBotShutdown(Entity<ActivelyWrangledBotComponent> entity, ref ComponentShutdown args)
    {
        if (entity.Comp.UserUid is not { } userUid ||
            !TryComp<BotWranglerComponent>(userUid, out var botWranglerComponent))
            return;

        botWranglerComponent.WrangledBotUids.Remove(entity);
        AfterActivelyWrangledBotShutdown(entity);
    }

    private void OnBotWranglerShutdown(Entity<BotWranglerComponent> entity, ref ComponentShutdown args)
    {
        foreach (var botUid in entity.Comp.WrangledBotUids)
        {
            if (!TryComp<ActivelyWrangledBotComponent>(botUid, out var activelyWrangledBotComponent))
                continue;

            activelyWrangledBotComponent.UserUid = null;
            RemComp(botUid, activelyWrangledBotComponent);
        }
    }

    //when you want to select a bot to wrangle
    private void OnSelectControlledBot(Entity<BotWranglerComponent> entity, ref SelectControlledBotEvent args)
    {
        if (!_gameTiming.IsFirstTimePredicted ||
            args.Handled)
            return;

        args.Handled = true;

        var activelyWrangledBotComponent = EnsureComp<ActivelyWrangledBotComponent>(args.Target);
        if (activelyWrangledBotComponent.UserUid is { } oldUserUid)
        {
            // We clicked on a bot that we are already controlling
            if (oldUserUid == entity.Owner)
            {
                // Deselect the bot
                RemComp(args.Target, activelyWrangledBotComponent);
                return;
            }

            _popupSystem.PopupEntity(Loc.GetString("ai-bot-someone-else-selected"), args.Target, oldUserUid, PopupType.MediumCaution);
        }

        activelyWrangledBotComponent.UserUid = entity;
        Dirty(args.Target, activelyWrangledBotComponent);

        entity.Comp.WrangledBotUids.Add(args.Target);
        Dirty(entity, entity.Comp);

        _popupSystem.PopupClient(Loc.GetString("ai-bot-selection-successful"), args.Performer, PopupType.Medium);
    }

    //when you want to move selected bot
    private void OnMoveControlledBotToPosition(Entity<BotWranglerComponent> entity, ref MoveControlledBotToPositionEvent args)
    {
        if (entity.Comp.WrangledBotUids.Count == 0)
        {
            _popupSystem.PopupClient(Loc.GetString("ai-controlled-bot-not-found"), args.Performer, PopupType.Medium);
            return;
        }

        _popupSystem.PopupClient(Loc.GetString("ai-bot-targeting-successful"), args.Performer, PopupType.Medium);

        foreach (var botUid in entity.Comp.WrangledBotUids.ToArray())
        {
            TryMoveBot(botUid, args.Target);
            RemComp<ActivelyWrangledBotComponent>(botUid);
        }

        entity.Comp.WrangledBotUids.Clear();
        Dirty(entity, entity.Comp);
    }

    protected virtual void AfterActivelyWrangledBotShutdown(Entity<ActivelyWrangledBotComponent> entity) { }

    //server glue
    public virtual void TryMoveBot(EntityUid botUid, EntityCoordinates targetCoordinates) { }
}

/// <summary>
///     Invoked when the entity target action ActionSelectControlledBot is called.
/// </summary>
public sealed partial class SelectControlledBotEvent : EntityTargetActionEvent;

/// <summary>
///     Invoked when the entity target action ActionMoveControlledBot is called.
/// </summary>
public sealed partial class MoveControlledBotToPositionEvent : WorldTargetActionEvent;
