using System.Linq;
using System.Numerics;
using Content.Client._KS14.Actions.UI;
using Content.Client.UserInterface.Systems.Actions.Controls;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.ContentPack;
using Robust.Shared.Utility;

namespace Content.Client.UserInterface.Systems.Actions;

/// <summary>
///     Arranging the action bar and the floating buttons. A padlock beside the top of the bar locks or unlocks the
///         layout: unlocked (the default), an action on the bar or loose on the screen can be dragged to another slot,
///         off the bar to set it loose, or onto the bin; locked, every one of them is just a button, and alt-click,
///         right-click and dragging leave the layout alone. Shift-click (keys) and ctrl-click (quick-cast) work either
///         way. Dragging out of the actions menu always works, since opening the menu is asking to change things.
/// </summary>
/// <remarks>
///     An action only ever leaves the screen two ways: dropped on the bin, which shows beside the top of the bar
///         while any action is being dragged, or dragged back into the actions menu. Nothing else takes one away:
///         dropping one off the bar sets it loose instead, and right-click no longer clears a slot.
/// </remarks>
public sealed partial class ActionUIController
{
    private static readonly ResPath ArrangeLockPath = new("/ks_action_arrange_lock.txt");

    /// <summary>
    ///     Space between the bar and the padlock or the bin.
    /// </summary>
    private const float ArrangeOverlayGap = 4f;

    private bool _arrangeLocked;
    private bool _arrangeLockLoaded;
    private KsActionArrangeLockButton? _arrangeLockButton;
    private KsActionTrashSlot? _trashSlot;

    /// <summary>
    ///     Whether the action layout is locked against dragging.
    /// </summary>
    public bool ArrangeLocked
    {
        get
        {
            EnsureArrangeLockLoaded();
            return _arrangeLocked;
        }
    }

    /// <summary>
    ///     Lock or unlock the action layout.
    /// </summary>
    public void SetArrangeLocked(bool locked)
    {
        EnsureArrangeLockLoaded();
        _arrangeLocked = locked;
        _arrangeLockButton?.SetLocked(locked);
        if (locked && _menuDragHelper.Dragged is { } dragged && !IsMenuButton(dragged))
            _menuDragHelper.EndDrag();

        _resourceManager.UserData.WriteAllText(ArrangeLockPath, locked ? "locked" : "unlocked");
    }

    private void EnsureArrangeLockLoaded()
    {
        if (_arrangeLockLoaded)
            return;

        _arrangeLockLoaded = true;
        _arrangeLocked = _resourceManager.UserData.TryReadAllText(ArrangeLockPath, out var text) && text.Trim() == "locked";
    }

    /// <summary>
    ///     Whether something that changes the layout may go ahead; says why not if it can't.
    /// </summary>
    private bool KsCheckArrangeUnlocked()
    {
        if (!ArrangeLocked)
            return true;

        Popup(Loc.GetString("ks-action-arrange-is-locked"));
        return false;
    }

    /// <summary>
    ///     Whether pressing this button may start a drag. Bar and floating buttons follow the lock; the actions
    ///         menu's never do.
    /// </summary>
    private bool KsCanStartDrag(ActionButton button)
    {
        return IsMenuButton(button) || !ArrangeLocked;
    }

    private bool IsMenuButton(ActionButton button)
    {
        return _window != null && button.Parent == _window.ResultsGrid;
    }

    private bool IsInside(Control? control, Control? ancestor)
    {
        for (var current = control; current != null && ancestor != null; current = current.Parent)
        {
            if (current == ancestor)
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Puts the padlock and the bin on the game screen, beside the bar.
    /// </summary>
    private void AttachArrangeOverlays()
    {
        if (UIManager.ActiveScreen is not { } screen)
            return;

        _arrangeLockButton ??= CreateArrangeLockButton();
        _trashSlot ??= new KsActionTrashSlot { Visible = false };

        _arrangeLockButton.Orphan();
        _trashSlot.Orphan();
        screen.AddChild(_arrangeLockButton);
        screen.AddChild(_trashSlot);
        _arrangeLockButton.SetLocked(ArrangeLocked);
        UpdateArrangeOverlays();
    }

    private KsActionArrangeLockButton CreateArrangeLockButton()
    {
        var button = new KsActionArrangeLockButton();
        button.OnPressed += _ =>
        {
            SetArrangeLocked(!ArrangeLocked);
            Popup(Loc.GetString(ArrangeLocked ? "ks-action-arrange-locked" : "ks-action-arrange-unlocked"));
        };
        return button;
    }

    /// <summary>
    ///     The area the bar's slots cover, in UI units from the top left of the screen. Measured from the slots
    ///         themselves: across the screen, the bar's grid is held to one slot's width while its slots run on past it.
    /// </summary>
    /// <param name="filledOnly">Leave out empty slots (the one at the end only shows during a drag).</param>
    private static UIBox2 BarRect(ActionButtonContainer container, bool filledOnly)
    {
        Vector2? topLeft = null;
        var bottomRight = container.GlobalPosition;
        foreach (var slot in container.GetButtons())
        {
            if (filledOnly && slot.Action == null)
                continue;

            topLeft = topLeft is { } corner ? Vector2.Min(corner, slot.GlobalPosition) : slot.GlobalPosition;
            bottomRight = Vector2.Max(bottomRight, slot.GlobalPosition + slot.Size);
        }

        return topLeft is { } found
            ? new UIBox2(found, bottomRight)
            : UIBox2.FromDimensions(container.GlobalPosition, new Vector2(FloatingActionHalfSize.X * 2, 0));
    }

    /// <summary>
    ///     Where an alt-clicked slot's floating button goes: just clear of the bar (and of the bin's column) level
    ///         with the slot, beside a bar that runs down the screen or under one that runs across it, stepped along
    ///         past any floating button already there.
    /// </summary>
    private Vector2 FloatingSpotBeside(ActionButton slot)
    {
        if (_container == null)
            return UIManager.MousePositionScaled.Position - FloatingActionHalfSize;

        var bar = BarRect(_container, filledOnly: false);
        var vertical = bar.Height >= bar.Width;
        var step = vertical ? new Vector2(68, 0) : new Vector2(0, 68);
        var position = vertical
            ? new Vector2(bar.Right + ArrangeOverlayGap * 2 + FloatingActionHalfSize.X * 2, slot.GlobalPosition.Y)
            : new Vector2(slot.GlobalPosition.X, bar.Bottom + ArrangeOverlayGap * 2);

        for (var tries = 0; tries < 8 && _floatingActions.Any(w => (w.Position - position).Length() < 16); tries++)
            position += step;

        return position;
    }

    private void DetachArrangeOverlays()
    {
        _arrangeLockButton?.Orphan();
        _trashSlot?.Orphan();
    }

    /// <summary>
    ///     Keeps the padlock and the bin just to the right of the top of the bar, wherever the bar has gone. Both sit
    ///         over the screen rather than in the bar, so neither pushes a slot aside. The bin only shows during a drag,
    ///         and takes the padlock's place while it does.
    /// </summary>
    private void UpdateArrangeOverlays()
    {
        if (_arrangeLockButton?.Parent is not { } screen || _trashSlot == null)
            return;

        if (_container is not { VisibleInTree: true, ChildCount: > 0 } container)
        {
            _arrangeLockButton.Visible = false;
            _trashSlot.Visible = false;
            return;
        }

        // The bin clears the empty slot a drag opens at the end of the bar; the padlock only has to clear the actions.
        var bar = BarRect(container, filledOnly: false);
        var filled = BarRect(container, filledOnly: true);
        MoveIfMoved(_trashSlot, new Vector2(bar.Right + ArrangeOverlayGap, bar.Top) - screen.GlobalPosition);
        MoveIfMoved(_arrangeLockButton, new Vector2(filled.Right + ArrangeOverlayGap, filled.Top) - screen.GlobalPosition);

        var dragging = _menuDragHelper.IsDragging;
        _arrangeLockButton.Visible = !dragging;
        _trashSlot.Visible = dragging;
    }

    /// <summary>
    ///     Runs every frame, so it only touches the layout when the bar has actually moved.
    /// </summary>
    private static void MoveIfMoved(Control control, Vector2 position)
    {
        if ((control.Position - position).LengthSquared() > 0.01f)
            LayoutContainer.SetPosition(control, position);
    }

    /// <summary>
    ///     Lights the bin while a dragged action is over it.
    /// </summary>
    private void UpdateTrashHover()
    {
        if (_trashSlot == null)
            return;

        var hovered = UIManager.MouseGetControl(_input.MouseScreenPosition);
        var overTrash = _trashSlot.Visible && IsInside(hovered, _trashSlot);
        _trashSlot.SetHovered(overTrash);

        // The dragged icon is as big as the bin, so it goes see-through over it, or the bin can't be seen lighting up.
        _dragShadow.Modulate = overTrash ? Color.White.WithAlpha(0.45f) : Color.White;
    }

    /// <summary>
    ///     Handles a drop anywhere but a slot on the bar: the bin, the actions menu, or the screen.
    /// </summary>
    /// <returns>True if the drop was handled here; false for a drop onto a bar slot, which the caller handles.</returns>
    private bool TryDropOffBar(ActionButton dragged, Control? hovered)
    {
        if (dragged.Action is not { } action)
            return false;

        if (hovered is ActionButton target && target.Parent == _container)
            return false;

        var actionUid = action.Owner;
        var fromMenu = IsMenuButton(dragged);
        var floating = FloatingWindowOf(dragged);

        if (_trashSlot != null && IsInside(hovered, _trashSlot))
        {
            HideAction(actionUid);
            return true;
        }

        // Dragged back into the actions menu: put away, the same as the bin. A drag out of the menu that comes
        //     back into it changes nothing.
        if (_window is { IsOpen: true } && IsInside(hovered, _window))
        {
            if (!fromMenu)
                HideAction(actionUid);

            return true;
        }

        // Anywhere else on the screen: loose, right there.
        var position = UIManager.MousePositionScaled.Position - FloatingActionHalfSize;
        if (floating != null)
            MoveFloatingAction(floating, position);
        else
            FloatActionAt(actionUid, position);

        return true;
    }

    /// <summary>
    ///     Before an action from the menu or a floating button lands on a bar slot: takes it from wherever else it
    ///         sits, so it's never in two places.
    /// </summary>
    /// <returns>
    ///     The button to treat as the one dragged: its own slot, if the menu's action is already shown on the bar,
    ///         so the drop moves it the way a drag along the bar would.
    /// </returns>
    private ActionButton PrepareDropOntoBar(ActionButton dragged)
    {
        if (dragged.Action is not { } action || dragged.Parent == _container)
            return dragged;

        var actionUid = action.Owner;
        if (FloatingWindowOf(actionUid) is { } window)
            CloseFloatingAction(window, forget: true);

        if (!IsMenuButton(dragged) || _container == null)
            return dragged;

        foreach (var slot in _container.GetButtons())
        {
            if (slot.Action?.Owner == actionUid)
                return slot;
        }

        RemoveActionFromFolders(actionUid);
        return dragged;
    }

    /// <summary>
    ///     A drop onto a slot that pushed another action out, when the dragged one didn't come from the bar: the
    ///         pushed-out one goes where the dragged one was, loose on the screen, or else to the end of the bar. It
    ///         never just vanishes.
    /// </summary>
    private void PlaceDisplacedAction(EntityUid? displaced, EntityUid placed, Vector2? floatingPosition)
    {
        if (displaced is not { } displacedUid || displacedUid == placed || ContainsAssignedAction(displacedUid))
            return;

        if (floatingPosition is { } position)
            FloatActionAt(displacedUid, position);
        else
            AddActionToRoot(displacedUid);
    }

    /// <summary>
    ///     Takes an action off the bar and off the screen. The actions menu still has it.
    /// </summary>
    private void HideAction(EntityUid actionUid)
    {
        var window = FloatingWindowOf(actionUid);
        if (window != null)
            CloseFloatingAction(window, forget: true);
        else if (!ContainsAssignedAction(actionUid))
            return;

        RemoveActionFromFolders(actionUid);
        SaveActionConfigurationAfterChange();
        RefreshActionBar();
        var name = EntityManager.GetComponentOrNull<MetaDataComponent>(actionUid)?.EntityName ?? "";
        Popup(Loc.GetString("ks-action-hidden", ("art", FormattedMessage.RemoveMarkupPermissive(name))));
    }
}
