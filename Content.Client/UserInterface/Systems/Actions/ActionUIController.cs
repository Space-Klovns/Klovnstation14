using System.Linq;
using System.Numerics;
using Content.Client.Actions;
using Content.Client.Construction;
using Content.Client.Gameplay;
using Content.Client.Hands;
using Content.Client.Interaction;
using Content.Client.Outline;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.Actions.Controls;
using Content.Client.UserInterface.Systems.Actions.Widgets;
using Content.Client.UserInterface.Systems.Actions.Windows;
using Content.Client.UserInterface.Systems.Gameplay;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Charges.Systems;
using Content.Shared.Input;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using static Content.Client.Actions.ActionsSystem;
using static Content.Client.UserInterface.Systems.Actions.Windows.ActionsWindow;
using static Robust.Client.UserInterface.Control;
using static Robust.Client.UserInterface.Controls.BaseButton;
using static Robust.Client.UserInterface.Controls.LineEdit;
using static Robust.Client.UserInterface.Controls.MultiselectOptionButton<
    Content.Client.UserInterface.Systems.Actions.Windows.ActionsWindow.Filters>;
using static Robust.Client.UserInterface.Controls.TextureRect;
using static Robust.Shared.Input.Binding.PointerInputCmdHandler;

namespace Content.Client.UserInterface.Systems.Actions;

public sealed partial class ActionUIController : UIController, IOnStateChanged<GameplayState>, IOnSystemChanged<ActionsSystem>
{
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private IInputManager _input = default!;

    [UISystemDependency] private readonly ActionsSystem? _actionsSystem = default;
    [UISystemDependency] private readonly InteractionOutlineSystem? _interactionOutline = default;
    [UISystemDependency] private readonly TargetOutlineSystem? _targetOutline = default;
    [UISystemDependency] private readonly SpriteSystem _spriteSystem = default!;

    private ActionButtonContainer? _container;
    private readonly List<EntityUid?> _actions = new();
    private readonly DragDropHelper<ActionButton> _menuDragHelper;
    private readonly TextureRect _dragShadow;
    private ActionsWindow? _window;

    private ActionsBar? ActionsBar => UIManager.GetActiveUIWidgetOrNull<ActionsBar>();
    private MenuButton? ActionButton => UIManager.GetActiveUIWidgetOrNull<MenuBar.Widgets.GameTopMenuBar>()?.ActionButton;

    public bool IsDragging => _menuDragHelper.IsDragging;

    /// <summary>
    /// Action slot we are currently selecting a target for.
    /// </summary>
    public EntityUid? SelectingTargetFor { get; private set; }

    public ActionUIController()
    {
        _menuDragHelper = new DragDropHelper<ActionButton>(OnMenuBeginDrag, OnMenuContinueDrag, OnMenuEndDrag);
        _dragShadow = new TextureRect
        {
            MinSize = new Vector2(64, 64),
            Stretch = StretchMode.Scale,
            Visible = false,
            SetSize = new Vector2(64, 64),
            MouseFilter = MouseFilterMode.Ignore
        };
    }

    public override void Initialize()
    {
        base.Initialize();

        var gameplayStateLoad = UIManager.GetUIController<GameplayStateLoadController>();
        gameplayStateLoad.OnScreenLoad += OnScreenLoad;
        gameplayStateLoad.OnScreenUnload += OnScreenUnload;
        InitializeActionConfigurationPersistence(); // KS14: persist action slots and folders in player data
    }

    private void OnScreenLoad()
    {
        LoadGui();
    }

    private void OnScreenUnload()
    {
        UnloadGui();
    }

    public void OnStateEntered(GameplayState state)
    {
        if (_actionsSystem != null)
        {
            _actionsSystem.OnActionAdded += OnActionAdded;
            _actionsSystem.OnActionRemoved += OnActionRemoved;
            _actionsSystem.ActionsUpdated += OnActionsUpdated;
        }

        UpdateFilterLabel();
        QueueWindowUpdate();

        _dragShadow.Orphan();
        UIManager.PopupRoot.AddChild(_dragShadow);

        var builder = CommandBinds.Builder;
        var hotbarKeys = ContentKeyFunctions.GetHotbarBoundKeys();
        for (var i = 0; i < hotbarKeys.Length; i++)
        {
            var boundId = i; // This is needed, because the lambda captures it.
            var boundKey = hotbarKeys[i];
            builder = builder.Bind(boundKey, new PointerInputCmdHandler((in PointerInputCmdArgs args) =>
            {
                if (args.State != BoundKeyState.Down)
                    return false;

                TriggerAction(boundId);
                return true;
            }, false, true));
        }

        builder
            .Bind(ContentKeyFunctions.OpenActionsMenu,
                InputCmdHandler.FromDelegate(_ => ToggleWindow()))
            .BindBefore(EngineKeyFunctions.Use, new PointerInputCmdHandler(TargetingOnUse, outsidePrediction: true),
                    typeof(ConstructionSystem), typeof(DragDropSystem))
                .BindBefore(EngineKeyFunctions.UIRightClick, new PointerInputCmdHandler(TargetingCancel, outsidePrediction: true))
            .Register<ActionUIController>();
    }

    private bool TargetingCancel(in PointerInputCmdArgs args)
    {
        if (!_timing.IsFirstTimePredicted)
            return false;

        // only do something for actual target-based actions
        if (SelectingTargetFor == null)
            return false;

        StopTargeting();
        return true;
    }

    /// <summary>
    ///     If the user clicked somewhere, and they are currently targeting an action, try and perform it.
    /// </summary>
    private bool TargetingOnUse(in PointerInputCmdArgs args)
    {
        if (!_timing.IsFirstTimePredicted || _actionsSystem == null || SelectingTargetFor is not { } actionId)
            return false;

        if (_playerManager.LocalEntity is not { } user)
            return false;

        if (!EntityManager.TryGetComponent<ActionsComponent>(user, out var comp))
            return false;

        if (_actionsSystem.GetAction(actionId) is not { } action ||
            !EntityManager.TryGetComponent<TargetActionComponent>(action, out var target))
        {
            return false;
        }

        // Is the action currently valid?
        if (!_actionsSystem.ValidAction(action))
        {
            // The user is targeting with this action, but it is not valid. Maybe mark this click as
            // handled and prevent further interactions.
            return !target.InteractOnMiss;
        }

        var ev = new ActionTargetAttemptEvent(args, (user, comp), action);
        EntityManager.EventBus.RaiseLocalEvent(action, ref ev);
        if (!ev.Handled)
        {
            Log.Error($"Action {EntityManager.ToPrettyString(actionId)} did not handle ActionTargetAttemptEvent!");
            return false;
        }

        // stop targeting when needed
        if (ev.FoundTarget ? !target.Repeat : target.DeselectOnMiss)
            StopTargeting();

        return true;
    }

    public void UnloadButton()
    {
        if (ActionButton != null)
            ActionButton.OnPressed -= ActionButtonPressed;
    }

    public void LoadButton()
    {
        if (ActionButton != null)
            ActionButton.OnPressed += ActionButtonPressed;
    }

    private void OnWindowOpened()
    {
        ActionButton?.SetClickPressed(true);

        SearchAndDisplay();
    }

    private void OnWindowClosed()
    {
        ActionButton?.SetClickPressed(false);
    }

    public void OnStateExited(GameplayState state)
    {
        CloseFloatingActions(); // KS14
        CancelSlotRebind(); // KS14

        if (_actionsSystem != null)
        {
            _actionsSystem.OnActionAdded -= OnActionAdded;
            _actionsSystem.OnActionRemoved -= OnActionRemoved;
            _actionsSystem.ActionsUpdated -= OnActionsUpdated;
        }

        CommandBinds.Unregister<ActionUIController>();
    }

    private void TriggerAction(int index)
    {
        if (!_actions.TryGetValue(index, out var actionId) ||
            _actionsSystem?.GetAction(actionId) is not { } action)
        {
            return;
        }

        // KS14 start: folder navigation is client-side, so it never reaches the action itself
        if (TryActivateFolderAction(action.Owner))
            return;
        // KS14 end

        // TODO: probably should have a clientside event raised for flexibility
        // KS14 start: some targeted actions need no aiming right now
        if (_actionsSystem?.KsTryUseWithoutAiming(action) == true)
            return;
        // KS14 end
        if (EntityManager.TryGetComponent<TargetActionComponent>(action, out var target))
            ToggleTargeting((action, action, target));
        else
            _actionsSystem?.TriggerAction(action);
    }

    private void OnActionAdded(EntityUid actionId)
    {
        if (_actionsSystem?.GetAction(actionId) is not { } action)
            return;

        // TODO: event
        // if the action is toggled when we add it, start targeting
        if (action.Comp.Toggled && EntityManager.TryGetComponent<TargetActionComponent>(actionId, out var target))
            StartTargeting((action, action, target));

        // KS14 start: replaces "if (_actions.Contains(action)) return; _actions.Add(action);".
        // An action that doesn't auto-populate stays attached but off the bar.
        if (KeepOffBar(action))
            return;

        // A late-granted action returns to its saved position if a saved layout is still pending,
        // otherwise it lands at folder level zero.
        if (TryApplyPendingActionConfiguration())
        {
            RefreshActionBar();
            return;
        }

        AddActionToRoot(action);
        SaveActionConfigurationAfterChange();
        // KS14 end
    }

    private void OnActionRemoved(EntityUid actionId)
    {
        if (_container == null)
            return;

        if (actionId == SelectingTargetFor)
            StopTargeting();

        RemoveActionFromFolders(actionId); // KS14: _actions.RemoveAll(x => x == actionId), now also clearing folders
        ForgetAutoPopulate(actionId); // KS14
        RemoveFloatingAction(actionId); // KS14
    }

    private void OnActionsUpdated()
    {
        QueueWindowUpdate();

        // KS14: replaces a direct _container.SetActionData. This fires on every change to any of the player's actions
        //     (a cooldown, greying out for want of qi), often many times a frame, so the bar is rebuilt once per frame
        //     instead - see ApplyActionsUpdate.
        _ksActionsUpdatePending = true;
    }

    // KS14 start
    private bool _ksActionsUpdatePending;

    private void ApplyActionsUpdate()
    {
        if (!_ksActionsUpdatePending)
            return;

        _ksActionsUpdatePending = false;
        // Action components can resolve after the action-set link, and the bar has to keep its folder entries.
        TryApplyPendingActionConfiguration();
        SyncAutoPopulate();
        RestoreFloatingActions();
        RefreshActionBar();
    }
    // KS14 end

    private void ActionButtonPressed(ButtonEventArgs args)
    {
        ToggleWindow();
    }

    private void ToggleWindow()
    {
        if (_window == null)
            return;

        if (_window.IsOpen)
        {
            _window.Close();
            return;
        }

        _window.Open();
    }

    private void UpdateFilterLabel()
    {
        if (_window == null)
            return;

        if (_window.FilterButton.SelectedKeys.Count == 0)
        {
            _window.FilterLabel.Visible = false;
        }
        else
        {
            _window.FilterLabel.Visible = true;
            _window.FilterLabel.Text = Loc.GetString("ui-actionmenu-filter-label",
                ("selectedLabels", string.Join(", ", _window.FilterButton.SelectedLabels)));
        }
    }

    private bool MatchesFilter(Entity<ActionComponent> ent, Filters filter)
    {
        var (uid, comp) = ent;
        return filter switch
        {
            Filters.Enabled => comp.Enabled,
            Filters.Item => comp.Container != null && comp.Container != _playerManager.LocalEntity,
            Filters.Innate => comp.Container == null || comp.Container == _playerManager.LocalEntity,
            Filters.Instant => EntityManager.HasComponent<InstantActionComponent>(uid),
            Filters.Targeted => EntityManager.HasComponent<TargetActionComponent>(uid),
            _ => throw new ArgumentOutOfRangeException(nameof(filter), filter, null)
        };
    }

    private void ClearList()
    {
        if (_window?.Disposed == false)
            _window.ResultsGrid.RemoveAllChildren();
    }

    private void PopulateActions(IEnumerable<Entity<ActionComponent>> actions)
    {
        if (_window is not { Disposed: false, IsOpen: true })
            return;

        if (_actionsSystem == null)
            return;

        _window.UpdateNeeded = false;

        List<ActionButton> existing = new(_window.ResultsGrid.ChildCount);
        foreach (var child in _window.ResultsGrid.Children)
        {
            if (child is ActionButton button)
                existing.Add(button);
        }

        int i = 0;
        foreach (var action in actions)
        {
            if (i < existing.Count)
            {
                existing[i++].UpdateData(action, _actionsSystem);
                continue;
            }

            var button = new ActionButton(EntityManager, _spriteSystem, this) { Locked = true };
            button.ActionPressed += OnWindowActionPressed;
            button.ActionUnpressed += OnWindowActionUnPressed;
            button.ActionFocusExited += OnWindowActionFocusExisted;
            button.UpdateData(action, _actionsSystem);
            _window.ResultsGrid.AddChild(button);
        }

        for (; i < existing.Count; i++)
        {
            existing[i].Dispose();
        }
    }

    public void QueueWindowUpdate()
    {
        if (_window != null)
            _window.UpdateNeeded = true;
    }

    private void SearchAndDisplay()
    {
        if (_window is not { Disposed: false, IsOpen: true })
            return;

        if (_actionsSystem == null)
            return;

        if (_playerManager.LocalEntity is not { } player)
            return;

        var search = _window.SearchBar.Text;
        var filters = _window.FilterButton.SelectedKeys;
        var actions = _actionsSystem.GetClientActions();

        if (filters.Count == 0 && string.IsNullOrWhiteSpace(search))
        {
            PopulateActions(actions);
            return;
        }

        actions = actions.Where(action =>
        {
            if (filters.Count > 0 && filters.Any(filter => !MatchesFilter(action, filter)))
                return false;

            if (action.Comp.Keywords.Any(keyword => search.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                return true;

            var name = EntityManager.GetComponent<MetaDataComponent>(action).EntityName;
            if (name.Contains(search, StringComparison.OrdinalIgnoreCase))
                return true;

            if (action.Comp.Container == null || action.Comp.Container == player)
                return false;

            var providerName = EntityManager.GetComponent<MetaDataComponent>(action.Comp.Container.Value).EntityName;
            return providerName.Contains(search, StringComparison.OrdinalIgnoreCase);
        });

        PopulateActions(actions);
    }

    private void SetAction(ActionButton button, EntityUid? actionId, bool updateSlots = true)
    {
        if (_actionsSystem == null)
            return;

        int position;

        if (actionId == null)
        {
            button.ClearData();
            if (_container?.TryGetButtonIndex(button, out position) ?? false)
            {
                if (_actions.Count > position && position >= 0)
                    _actions.RemoveAt(position);
            }
        }
        else if (button.TryReplaceWith(actionId.Value, _actionsSystem) &&
            _container != null &&
            _container.TryGetButtonIndex(button, out position))
        {
            if (position >= _actions.Count)
            {
                _actions.Add(actionId);
            }
            else
            {
                _actions[position] = actionId;
            }
        }

        SyncOpenActionFolder(); // KS14: persist edits made within the open folder

        // KS14 start: replaces "_container?.SetActionData(_actionsSystem, _actions.ToArray());" - the
        // bar is drawn through the folder-aware path, and user edits are committed as they happen.
        if (updateSlots)
        {
            RefreshActionBar();
            SaveActionConfigurationAfterChange();
        }
        // KS14 end
    }

    private void DragAction()
    {
        if (_menuDragHelper.Dragged is not { Action: { } action } dragged)
        {
            _menuDragHelper.EndDrag();
            return;
        }

        EntityUid? swapAction = null;
        var currentlyHovered = UIManager.MouseGetControl(_input.MouseScreenPosition);
        // KS14 start: a drop on the bin, into the actions menu or anywhere off the bar is handled in the arrange
        // partial (upstream cleared the slot of anything dropped off the bar). An action coming onto the bar from the
        // menu or from a floating button is taken from wherever else it sits first, so it is never in two places.
        if (TryDropOffBar(dragged, currentlyHovered) ||
            currentlyHovered is ActionButton exitSlot && IsExitFolderSlot(exitSlot))
        {
            _menuDragHelper.EndDrag();
            return;
        }

        var floatingPosition = FloatingWindowOf(dragged)?.Position;
        dragged = PrepareDropOntoBar(dragged);
        // KS14 end

        // KS14 start: dropping onto an action or a folder groups them, rather than swapping slots.
        // The back entry's slot refuses drops outright - see IsExitFolderSlot.
        if (currentlyHovered is ActionButton targetButton &&
            (IsExitFolderSlot(targetButton) ||
             TryAddActionToFolder(dragged, targetButton) ||
             TryCreateActionFolder(dragged, targetButton)))
        {
            _menuDragHelper.EndDrag();
            return;
        }
        // KS14 end

        if (currentlyHovered is ActionButton button)
        {
            swapAction = button.Action;
            SetAction(button, action, false);
        }

        if (dragged.Parent is ActionButtonContainer)
            SetAction(dragged, swapAction, false);
        else
            PlaceDisplacedAction(swapAction, action.Owner, floatingPosition); // KS14: never drop the pushed-out action

        // KS14 start: replaces a direct _container.SetActionData, to include client-only folder
        // entries and commit the completed drag.
        RefreshActionBar();
        SaveActionConfigurationAfterChange();
        // KS14 end

        _menuDragHelper.EndDrag();
    }

    private void OnClearPressed(ButtonEventArgs args)
    {
        if (_window == null)
            return;

        _window.SearchBar.Clear();
        _window.FilterButton.DeselectAll();
        UpdateFilterLabel();
        QueueWindowUpdate();
    }

    private void OnSearchChanged(LineEditEventArgs args)
    {
        QueueWindowUpdate();
    }

    private void OnFilterSelected(ItemPressedEventArgs args)
    {
        UpdateFilterLabel();
        QueueWindowUpdate();
    }

    private void OnWindowActionPressed(GUIBoundKeyEventArgs args, ActionButton action)
    {
        if (args.Function != EngineKeyFunctions.UIClick && args.Function != EngineKeyFunctions.Use)
            return;

        HandleActionPressed(args, action);
    }

    private void OnWindowActionUnPressed(GUIBoundKeyEventArgs args, ActionButton dragged)
    {
        if (args.Function != EngineKeyFunctions.UIClick && args.Function != EngineKeyFunctions.Use)
            return;

        HandleActionUnpressed(args, dragged);
    }

    private void OnWindowActionFocusExisted(ActionButton button)
    {
        _menuDragHelper.EndDrag();
    }

    private void OnActionPressed(GUIBoundKeyEventArgs args, ActionButton button)
    {
        if (args.Function == EngineKeyFunctions.UIRightClick)
        {
            // KS14 start: right-clicking a folder dissolves it and returns its members to the bar;
            // the back entry ignores it entirely. While the layout is locked it does nothing.
            if (IsDissolvableFolder(button) && !KsCheckArrangeUnlocked())
            {
                args.Handle();
                return;
            }

            if (TryHandleFolderRightClick(button))
            {
                args.Handle();
                return;
            }
            // KS14 end

            // KS14: upstream cleared the slot here ("SetAction(button, null);"). An action now leaves the bar only
            // through the bin or the actions menu, so a stray right-click can't lose one.
            args.Handle();
            return;
        }

        // KS14 start: shift-click a slot to bind it to a key, alt-click to pop it out as a floating button. Those
        // clicks arrive as examine and alt-use, never as a plain UIClick.
        if (args.Function == EngineKeyFunctions.TextCursorSelect || args.Function == ContentKeyFunctions.ExamineEntity)
        {
            if (TryStartSlotRebind(button))
                args.Handle();
            return;
        }

        if (args.Function == ContentKeyFunctions.AltActivateItemInWorld)
        {
            if (TryFloatAction(button))
                args.Handle();
            return;
        }

        // ctrl-click: quick-cast on or off
        if (args.Function == ContentKeyFunctions.TryPullObject || args.Function == EngineKeyFunctions.EditorGridPlace)
        {
            if (TryToggleQuickCast(button))
                args.Handle();
            return;
        }
        // KS14 end

        if (args.Function != EngineKeyFunctions.UIClick)
            return;

        HandleActionPressed(args, button);
    }

    private void HandleActionPressed(GUIBoundKeyEventArgs args, ActionButton button)
    {
        args.Handle();
        // KS14 start: folders and the back entry cannot be dragged
        if (IsFolderAction(button))
            return;

        // a locked layout only uses actions (the actions menu still drags)
        if (!KsCanStartDrag(button))
            return;
        // KS14 end

        if (button.Action != null)
        {
            _menuDragHelper.MouseDown(button);
            return;
        }

        // good job
    }

    private void OnActionUnpressed(GUIBoundKeyEventArgs args, ActionButton button)
    {
        if (args.Function != EngineKeyFunctions.UIClick)
            return;

        HandleActionUnpressed(args, button);
    }

    private void HandleActionUnpressed(GUIBoundKeyEventArgs args, ActionButton button)
    {
        if (_actionsSystem == null)
            return;

        args.Handle();

        if (_menuDragHelper.IsDragging)
        {
            DragAction();
            return;
        }

        _menuDragHelper.EndDrag();

        if (button.Action is not { } action)
            return;

        // KS14 start: enter a folder, or step back out to the root bar
        if (TryActivateFolderAction(action.Owner))
            return;
        // KS14 end

        // TODO: make this an event
        if (!EntityManager.TryGetComponent<TargetActionComponent>(action, out var target))
        {
            _actionsSystem?.TriggerAction(action);
            return;
        }

        // KS14 start: some targeted actions need no aiming right now
        if (_actionsSystem?.KsTryUseWithoutAiming(action) == true)
            return;
        // KS14 end

        // for target actions, we go into "select target" mode, we don't
        // message the server until we actually pick our target.

        // if we're clicking the same thing we're already targeting for, then we simply cancel
        // targeting
        ToggleTargeting((action, action.Comp, target));
    }

    private bool OnMenuBeginDrag()
    {
        // TODO ACTIONS
        // The dragging icon shuld be based on the entity's icon style. I.e. if the action has a large icon texture,
        // and a small item/provider sprite, then the dragged icon should be the big texture, not the provider.
        if (_menuDragHelper.Dragged?.Action is { } action)
        {
            if (EntityManager.TryGetComponent(action.Comp.EntityIcon, out SpriteComponent? sprite)
                && sprite.Icon?.GetFrame(RsiDirection.South, 0) is { } frame)
            {
                _dragShadow.Texture = frame;
            }
            else if (action.Comp.Icon is { } icon)
            {
                _dragShadow.Texture = _spriteSystem.Frame0(icon);
            }
            else
            {
                _dragShadow.Texture = null;
            }
        }

        LayoutContainer.SetPosition(_dragShadow, UIManager.MousePositionScaled.Position - new Vector2(32, 32));

        // KS14 start: a floating button being moved fades where it was until it's dropped
        if (_menuDragHelper.Dragged is { } draggedButton && FloatingWindowOf(draggedButton) is { } floating)
            floating.Modulate = Color.White.WithAlpha(0.35f);
        // KS14 end
        return true;
    }

    private bool OnMenuContinueDrag(float frameTime)
    {
        LayoutContainer.SetPosition(_dragShadow, UIManager.MousePositionScaled.Position - new Vector2(32, 32));
        _dragShadow.Visible = true;
        UpdateTrashHover(); // KS14
        return true;
    }

    private void OnMenuEndDrag()
    {
        _dragShadow.Texture = null;
        _dragShadow.Visible = false;

        // KS14 start
        foreach (var floating in _floatingActions)
            floating.Modulate = Color.White;

        _trashSlot?.SetHovered(false);
        _dragShadow.Modulate = Color.White;
        // KS14 end
    }

    private void UnloadGui()
    {
        _actionsSystem?.UnlinkAllActions();
        DetachArrangeOverlays(); // KS14

        if (ActionsBar == null)
        {
            return;
        }

        if (_window != null)
        {
            _window.OnOpen -= OnWindowOpened;
            _window.OnClose -= OnWindowClosed;
            _window.ClearButton.OnPressed -= OnClearPressed;
            _window.SearchBar.OnTextChanged -= OnSearchChanged;
            _window.FilterButton.OnItemSelected -= OnFilterSelected;

            _window.Dispose();
            _window = null;
        }
    }

    private void LoadGui()
    {
        UnloadGui();
        _window = UIManager.CreateWindow<ActionsWindow>();
        LayoutContainer.SetAnchorPreset(_window, LayoutContainer.LayoutPreset.CenterTop);

        _window.OnOpen += OnWindowOpened;
        _window.OnClose += OnWindowClosed;
        _window.ClearButton.OnPressed += OnClearPressed;
        _window.SearchBar.OnTextChanged += OnSearchChanged;
        _window.FilterButton.OnItemSelected += OnFilterSelected;

        if (ActionsBar == null)
        {
            return;
        }

        RegisterActionContainer(ActionsBar.ActionsContainer);
        AttachArrangeOverlays(); // KS14: the padlock and the bin beside the bar

        _actionsSystem?.LinkAllActions();
    }

    public void RegisterActionContainer(ActionButtonContainer container)
    {
        if (_container != null)
        {
            _container.ActionPressed -= OnActionPressed;
            _container.ActionUnpressed -= OnActionUnpressed;
        }

        _container = container;
        _container.ActionPressed += OnActionPressed;
        _container.ActionUnpressed += OnActionUnpressed;
    }

    private void ClearActions()
    {
        _container?.ClearActionData();
    }

    private void AssignSlots(List<SlotAssignment> assignments)
    {
        if (_actionsSystem == null)
            return;

        ResetActionFolders(); // KS14: loaded assignments replace transient folders
        _actions.Clear();
        foreach (var assign in assignments)
        {
            _actions.Add(assign.ActionId);
        }

        RefreshActionBar(); // KS14: route rendering through folder-aware path
    }

    public void RemoveActionContainer()
    {
        _container = null;
    }

    public void OnSystemLoaded(ActionsSystem system)
    {
        system.LinkActions += OnComponentLinked;
        system.UnlinkActions += OnComponentUnlinked;
        system.ClearAssignments += ClearActions;
        system.AssignSlot += AssignSlots;
    }

    public void OnSystemUnloaded(ActionsSystem system)
    {
        system.LinkActions -= OnComponentLinked;
        system.UnlinkActions -= OnComponentUnlinked;
        system.ClearAssignments -= ClearActions;
        system.AssignSlot -= AssignSlots;
    }

    public override void FrameUpdate(FrameEventArgs args)
    {
        _menuDragHelper.Update(args.DeltaSeconds);
        if (_window is { UpdateNeeded: true })
            SearchAndDisplay();

        ApplyActionsUpdate(); // KS14: batch bar rebuilds to one per frame
        FlushPendingActionConfigurationSave(); // KS14: batch layout writes to one per frame
        UpdateArrangeOverlays(); // KS14: keep the padlock and the bin beside the bar
    }

    private void OnComponentLinked(ActionsComponent component)
    {
        if (_actionsSystem == null)
            return;

        ResetActionFolders(); // KS14: folders belong to the currently linked action set
        LoadDefaultActions();
        BeginLinkedActionConfiguration(); // KS14: restore stable action and folder assignments
        RestoreFloatingActions(); // KS14: and the floating buttons
        RefreshActionBar(); // KS14: route rendering through folder-aware path
        QueueWindowUpdate();
    }

    private void OnComponentUnlinked()
    {
        EndLinkedActionConfiguration(); // KS14: flush any pending save, then drop the linked action set
        ResetActionFolders(); // KS14: discard folders tied to the previous action set
        // KS14: the old body's floating buttons go too (their places are remembered, so they come back for whoever
        //     has those arts next), and a half-done rebind is dropped rather than eating the next key.
        CloseFloatingActions();
        CancelSlotRebind();
        _ksActionsUpdatePending = false;
        _container?.ClearActionData();
        QueueWindowUpdate();
        StopTargeting();
    }

    private void LoadDefaultActions()
    {
        if (_actionsSystem == null)
            return;

        var actions = _actionsSystem.GetClientActions().Where(action => action.Comp.AutoPopulate).ToList();
        actions.Sort(ActionComparer);

        _actions.Clear();
        foreach (var (action, _) in actions)
        {
            if (!_actions.Contains(action))
                _actions.Add(action);
        }
    }

    /// <summary>
    /// If currently targeting with this slot, stops targeting.
    /// If currently targeting with no slot or a different slot, switches to
    /// targeting with the specified slot.
    /// </summary>
    private void ToggleTargeting(Entity<ActionComponent, TargetActionComponent> ent)
    {
        if (SelectingTargetFor == ent)
        {
            StopTargeting();
            return;
        }

        StartTargeting(ent);
    }

    /// <summary>
    /// Puts us in targeting mode, where we need to pick either a target point or entity
    /// </summary>
    private void StartTargeting(Entity<ActionComponent, TargetActionComponent> ent)
    {
        var (uid, action, target) = ent;

        // If we were targeting something else we should stop
        StopTargeting();

        SelectingTargetFor = uid;
        // TODO inform the server
        _actionsSystem?.SetToggled(uid, true);

        // override "held-item" overlay
        var provider = action.Container;

        if (target.TargetingIndicator && _overlays.TryGetOverlay<ShowHandItemOverlay>(out var handOverlay))
        {
            if (action.ItemIconStyle == ItemActionIconStyle.BigItem && action.Container != null)
            {
                handOverlay.EntityOverride = provider;
            }
            else if (action.Toggled && action.IconOn != null)
                handOverlay.IconOverride = _spriteSystem.Frame0(action.IconOn);
            else if (action.Icon != null)
                handOverlay.IconOverride = _spriteSystem.Frame0(action.Icon);
        }

        if (_container != null)
        {
            foreach (var button in _container.GetButtons())
            {
                if (button.Action?.Owner == uid)
                    button.UpdateIcons();
            }
        }

        // TODO: allow world-targets to check valid positions. E.g., maybe:
        // - Draw a red/green ghost entity
        // - Add a yes/no checkmark where the HandItemOverlay usually is

        // Highlight valid entity targets
        if (!EntityManager.TryGetComponent<EntityTargetActionComponent>(uid, out var entity))
            return;

        Func<EntityUid, bool>? predicate = null;
        var attachedEnt = action.AttachedEntity;

        if (!entity.CanTargetSelf)
            predicate = e => e != attachedEnt;

        var range = target.CheckCanAccess ? target.Range : -1;

        _interactionOutline?.SetEnabled(false);
        _targetOutline?.Enable(range, target.CheckCanAccess, predicate, entity.Whitelist, entity.Blacklist, null);
    }

    /// <summary>
    /// Switch out of targeting mode if currently selecting target for an action
    /// </summary>
    private void StopTargeting()
    {
        if (SelectingTargetFor == null)
            return;

        var oldAction = SelectingTargetFor;
        // TODO inform the server
        _actionsSystem?.SetToggled(oldAction, false);

        SelectingTargetFor = null;

        _targetOutline?.Disable();
        _interactionOutline?.SetEnabled(true);

        if (_container != null)
        {
            foreach (var button in _container.GetButtons())
            {
                if (button.Action?.Owner == oldAction)
                    button.UpdateIcons();
            }
        }

        if (!_overlays.TryGetOverlay<ShowHandItemOverlay>(out var handOverlay))
            return;

        handOverlay.IconOverride = null;
        handOverlay.EntityOverride = null;
    }
}
