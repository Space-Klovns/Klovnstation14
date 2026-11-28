using Content.Shared._KS14.Cultivation;
using Content.Shared._KS14.Cultivation.Body;
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
///         filed here and listed in one of four category menus (offensive, defensive, mobility, utility), whose buttons
///         are always on the bar; picking an art there uses it. Every art stays attached, so it can be used from the
///         menu, but only favourites (up to <see cref="CultivationLoadoutComponent.Slots"/>) also sit on the bar, chosen
///         in the Favourites menu. An art off the bar keeps its cooldown.
/// </summary>
public sealed partial class CultivationLoadoutSystem : EntitySystem
{
    public static readonly EntProtoId MenuAction = "ActionKsTechniques";

    public static readonly EntProtoId[] CategoryMenuActions =
    {
        "ActionKsArtsOffensive", "ActionKsArtsDefensive", "ActionKsArtsMobility", "ActionKsArtsUtility",
    };

    [Dependency] private SharedActionsSystem _actionsSystem = default!;
    [Dependency] private UserInterfaceSystem _uiSystem = default!;
    [Dependency] private CultivationNoticeSystem _noticeSystem = default!;

    private readonly Dictionary<EntityUid, List<EntityUid>> _justLearned = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_justLearned.Count == 0)
            return;

        foreach (var (uid, actions) in _justLearned)
        {
            if (TerminatingOrDeleted(uid))
                continue;

            var lines = new List<string>();
            foreach (var actionUid in actions)
            {
                if (TerminatingOrDeleted(actionUid))
                    continue;

                var category = CompOrNull<CultivationArtCategoryComponent>(actionUid)?.Category ?? CultivationArtCategory.Utility;
                lines.Add(Loc.GetString("cultivation-art-learned-line", ("technique", Name(actionUid)),
                    ("category", Loc.GetString($"cultivation-art-category-{category}"))));
            }

            if (lines.Count == 0)
                continue;

            _noticeSystem.Notify(uid, lines.Count == 1
                ? Loc.GetString("cultivation-art-learned-one", ("technique", Name(actions[^1])))
                : Loc.GetString("cultivation-art-learned-many", ("count", lines.Count)), PopupType.Medium);
            _noticeSystem.ChatOnly(uid, Loc.GetString("cultivation-art-learned-chat", ("list", string.Join("\n", lines))));
        }

        _justLearned.Clear();
    }

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
        {
            // Reached from the category menus, not the bar.
            loadout.MenuAction = _actionsSystem.AddAction(uid, MenuAction);
            if (loadout.MenuAction is { } menuAction)
                _actionsSystem.SetAutoPopulate(menuAction, false);
        }

        loadout.CategoryActions.RemoveAll(action => TerminatingOrDeleted(action));
        if (loadout.CategoryActions.Count == 0)
        {
            foreach (var categoryAction in CategoryMenuActions)
            {
                if (_actionsSystem.AddAction(uid, categoryAction) is { } added)
                    loadout.CategoryActions.Add(added);
            }

            Dirty(uid, loadout);
        }

        loadout.Slots = SlotsFor(uid);
        return (uid, loadout);
    }

    /// <summary>
    ///     How many techniques someone can keep ready: five, six at Golden Core or the sixth body stage, seven at Nascent
    ///         Soul or the ninth. Whichever path is further along counts.
    /// </summary>
    public int SlotsFor(EntityUid uid)
    {
        var slots = 5;
        if (TryComp<CultivatorComponent>(uid, out var cultivator))
            slots = Math.Max(slots, cultivator.Realm switch
            {
                CultivationRealm.NascentSoul => 7,
                CultivationRealm.GoldenCore => 6,
                _ => 5,
            });

        if (TryComp<BodyCultivatorComponent>(uid, out var body))
            slots = Math.Max(slots, body.Stage >= BodyCultivatorComponent.MaxStage ? 7 : body.Stage >= 6 ? 6 : 5);

        return slots;
    }

    /// <summary>
    ///     Their realm or stage changed: they may have room for more techniques now. Anything waiting fills the new room.
    /// </summary>
    public void RefreshSlots(EntityUid uid)
    {
        if (!TryComp<CultivationLoadoutComponent>(uid, out var loadout))
            return;

        Prune((uid, loadout));
        loadout.Slots = SlotsFor(uid);
        Dirty(uid, loadout);
    }

    private void OnLearned(Entity<ActionsComponent> entity, ref CultivationTechniqueLearnedEvent args)
    {
        var loadout = EnsureLoadout(entity);
        Prune(loadout);
        if (loadout.Comp.Learned.Contains(args.Action))
            return;

        loadout.Comp.Learned.Add(args.Action);
        // Learned into its category menu, not onto the bar: the bar stays the four menus and the player's favourites.
        SetOnBar(entity, args.Action, false);
        // Several are often learned at once (a breakthrough, a new law): they're told together, once, next tick.
        if (!_justLearned.TryGetValue(entity, out var learnedNow))
            _justLearned[entity] = learnedNow = new List<EntityUid>();
        learnedNow.Add(args.Action);

        Dirty(loadout);
    }

    private void OnMenu(Entity<CultivationLoadoutComponent> entity, ref TechniqueMenuActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        Prune(entity);
        // Always open it afresh in the middle of the screen, so a menu dragged away is never lost.
        _uiSystem.CloseUi(entity.Owner, TechniqueLoadoutUiKey.Key, args.Performer);
        _uiSystem.OpenUi(entity.Owner, TechniqueLoadoutUiKey.Key, args.Performer);
    }

    private void OnToggle(Entity<CultivationLoadoutComponent> entity, ref TechniqueLoadoutToggleMessage args)
    {
        if (!TryGetEntity(args.Action, out var actionUid) || !entity.Comp.Learned.Contains(actionUid.Value))
            return;

        // A pinned action can't be put away: picking it just makes sure it's on the bar.
        if (TryGetEntity(args.Action, out var pinnedUid) && entity.Comp.Pinned.Contains(pinnedUid.Value))
        {
            Pin(entity, pinnedUid.Value);
            return;
        }

        Prune(entity);
        if (entity.Comp.Equipped.Remove(actionUid.Value))
        {
            SetOnBar(entity, actionUid.Value, false);
        }
        else if (entity.Comp.Equipped.Count >= entity.Comp.Slots)
        {
            _noticeSystem.Notify(entity, Loc.GetString("cultivation-loadout-full", ("slots", entity.Comp.Slots)), PopupType.SmallCaution);
            return;
        }
        else
        {
            SetOnBar(entity, actionUid.Value, true);
            entity.Comp.Equipped.Add(actionUid.Value);
        }

        Dirty(entity);
    }

    /// <summary>
    ///     Forget techniques whose actions are gone (a forgotten law takes its techniques with it).
    /// </summary>
    /// <summary>
    ///     Keep an action on someone's bar for good, outside the slots, and list it in their Techniques menu. Puts it
    ///         back on the bar if it isn't there.
    /// </summary>
    public void Pin(EntityUid uid, EntityUid actionUid)
    {
        if (!HasComp<ActionsComponent>(uid))
            return;

        var loadout = EnsureLoadout(uid);
        if (!loadout.Comp.Pinned.Contains(actionUid))
            loadout.Comp.Pinned.Add(actionUid);

        SetOnBar(uid, actionUid, true);
        Dirty(loadout);
    }

    /// <summary>
    ///     Keep an art attached (so it can be used from its menu) and on or off the bar.
    /// </summary>
    private void SetOnBar(EntityUid uid, EntityUid actionUid, bool onBar)
    {
        if (!TryComp<ActionComponent>(actionUid, out var action) || action.AttachedEntity != uid)
            _actionsSystem.AddActionDirect(uid, actionUid);

        _actionsSystem.SetAutoPopulate(actionUid, onBar);
    }

    private void Prune(Entity<CultivationLoadoutComponent> entity)
    {
        var removed = entity.Comp.Learned.RemoveAll(uid => TerminatingOrDeleted(uid))
                      + entity.Comp.Equipped.RemoveAll(uid => TerminatingOrDeleted(uid))
                      + entity.Comp.Pinned.RemoveAll(uid => TerminatingOrDeleted(uid));
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
    ///     A technique has grown into a stronger one: the new one takes the old one's place, on the bar if it was there
    ///         (or pinned, if it was pinned), and the old one is gone.
    /// </summary>
    public void Replace(EntityUid uid, EntityUid oldActionUid, EntityUid newActionUid)
    {
        if (TerminatingOrDeleted(oldActionUid) || TerminatingOrDeleted(newActionUid))
            return;

        var loadout = EnsureLoadout(uid);
        if (!loadout.Comp.Learned.Contains(newActionUid))
            loadout.Comp.Learned.Add(newActionUid);

        if (loadout.Comp.Pinned.Remove(oldActionUid))
        {
            Pin(uid, newActionUid);
        }
        else if (loadout.Comp.Equipped.Contains(oldActionUid))
        {
            // Into the old one's slot, so a full bar doesn't push the upgrade into the menu.
            loadout.Comp.Equipped.Remove(newActionUid);
            loadout.Comp.Equipped.Insert(loadout.Comp.Equipped.IndexOf(oldActionUid), newActionUid);
            SetOnBar(uid, newActionUid, true);
        }

        Forget(uid, oldActionUid);
        _noticeSystem.Notify(uid, Loc.GetString("cultivation-technique-grown", ("old", Name(oldActionUid)), ("new", Name(newActionUid))),
            PopupType.Medium);
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
