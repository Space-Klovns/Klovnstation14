using Content.Shared._KS14.Cultivation.Components;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Popups;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Cultivation.Systems;

/// <summary>
///     Keeps a cultivator's action bar short. Every technique, body art, martial art and jianghu action they learn is
///         filed here; only <see cref="CultivationLoadoutComponent.Slots"/> of them sit on the bar at once. The
///         Techniques button opens a radial menu of everything they know, where a click equips or puts away a
///         technique. A put-away technique keeps its cooldown.
/// </summary>
public sealed partial class CultivationLoadoutSystem : EntitySystem
{
    public static readonly EntProtoId MenuAction = "ActionKsTechniques";

    [Dependency] private SharedActionsSystem _actionsSystem = default!;
    [Dependency] private UserInterfaceSystem _uiSystem = default!;
    [Dependency] private CultivationNoticeSystem _noticeSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ActionsComponent, CultivationTechniqueLearnedEvent>(OnLearned);
        SubscribeLocalEvent<CultivationLoadoutComponent, TechniqueMenuActionEvent>(OnMenu);
        Subs.BuiEvents<CultivationLoadoutComponent>(TechniqueLoadoutUiKey.Key, subs =>
        {
            subs.Event<TechniqueLoadoutToggleMessage>(OnToggle);
        });
    }

    private Entity<CultivationLoadoutComponent> EnsureLoadout(EntityUid uid)
    {
        var loadout = EnsureComp<CultivationLoadoutComponent>(uid);
        var ui = EnsureComp<UserInterfaceComponent>(uid);
        if (!_uiSystem.HasUi(uid, TechniqueLoadoutUiKey.Key, ui))
            _uiSystem.SetUi((uid, ui), TechniqueLoadoutUiKey.Key, new InterfaceData("TechniqueLoadoutBoundUserInterface"));

        if (loadout.MenuAction == null || TerminatingOrDeleted(loadout.MenuAction))
            loadout.MenuAction = _actionsSystem.AddAction(uid, MenuAction);

        return (uid, loadout);
    }

    private void OnLearned(Entity<ActionsComponent> entity, ref CultivationTechniqueLearnedEvent args)
    {
        var loadout = EnsureLoadout(entity);
        Prune(loadout);
        if (loadout.Comp.Learned.Contains(args.Action))
            return;

        loadout.Comp.Learned.Add(args.Action);
        if (loadout.Comp.Equipped.Count < loadout.Comp.Slots)
        {
            loadout.Comp.Equipped.Add(args.Action);
        }
        else
        {
            // No room on the bar: it waits in the Techniques menu.
            _actionsSystem.RemoveAction(entity.Owner, args.Action);
            _noticeSystem.Notify(entity, Loc.GetString("cultivation-loadout-stored", ("technique", Name(args.Action))), PopupType.Medium);
        }

        Dirty(loadout);
    }

    private void OnMenu(Entity<CultivationLoadoutComponent> entity, ref TechniqueMenuActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        Prune(entity);
        _uiSystem.TryToggleUi(entity.Owner, TechniqueLoadoutUiKey.Key, args.Performer);
    }

    private void OnToggle(Entity<CultivationLoadoutComponent> entity, ref TechniqueLoadoutToggleMessage args)
    {
        if (!TryGetEntity(args.Action, out var actionUid) || !entity.Comp.Learned.Contains(actionUid.Value))
            return;

        Prune(entity);
        if (entity.Comp.Equipped.Remove(actionUid.Value))
        {
            _actionsSystem.RemoveAction(entity.Owner, actionUid.Value);
        }
        else if (entity.Comp.Equipped.Count >= entity.Comp.Slots)
        {
            _noticeSystem.Notify(entity, Loc.GetString("cultivation-loadout-full", ("slots", entity.Comp.Slots)), PopupType.SmallCaution);
            return;
        }
        else
        {
            _actionsSystem.AddActionDirect(entity.Owner, actionUid.Value);
            entity.Comp.Equipped.Add(actionUid.Value);
        }

        Dirty(entity);
    }

    /// <summary>
    ///     Forget techniques whose actions are gone (a forgotten law takes its techniques with it).
    /// </summary>
    private void Prune(Entity<CultivationLoadoutComponent> entity)
    {
        var removed = entity.Comp.Learned.RemoveAll(uid => TerminatingOrDeleted(uid))
                      + entity.Comp.Equipped.RemoveAll(uid => TerminatingOrDeleted(uid));
        if (removed > 0)
            Dirty(entity);
    }

    /// <summary>
    ///     Whether a technique is on their bar right now.
    /// </summary>
    public bool IsEquipped(EntityUid uid, EntityUid actionUid)
    {
        return TryComp<CultivationLoadoutComponent>(uid, out var loadout) && loadout.Equipped.Contains(actionUid);
    }

    /// <summary>
    ///     Take a technique away for good, whether it's on the bar or put away.
    /// </summary>
    public void Forget(EntityUid uid, EntityUid? actionUid)
    {
        if (actionUid is not { } action || TerminatingOrDeleted(action))
            return;

        if (TryComp<CultivationLoadoutComponent>(uid, out var loadout))
        {
            loadout.Learned.Remove(action);
            loadout.Equipped.Remove(action);
            Dirty(uid, loadout);
        }

        if (TryComp<ActionComponent>(action, out var actionComponent) && actionComponent.AttachedEntity == uid)
            _actionsSystem.RemoveAction(uid, action);

        QueueDel(action);
    }

    /// <summary>
    ///     Whether they know a technique, equipped or put away.
    /// </summary>
    public bool Knows(EntityUid uid, EntProtoId action)
    {
        if (!TryComp<CultivationLoadoutComponent>(uid, out var loadout))
            return false;

        foreach (var actionUid in loadout.Learned)
        {
            if (!TerminatingOrDeleted(actionUid) && MetaData(actionUid).EntityPrototype?.ID == action.Id)
                return true;
        }

        return false;
    }

    /// <summary>
    ///     File a technique action someone has just been given.
    /// </summary>
    public void Learned(EntityUid uid, EntityUid actionUid)
    {
        var ev = new CultivationTechniqueLearnedEvent(actionUid);
        RaiseLocalEvent(uid, ref ev);
    }
}
