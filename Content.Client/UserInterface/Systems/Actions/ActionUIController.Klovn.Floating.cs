using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using Content.Client._KS14.Actions.UI;
using Content.Client.UserInterface.Systems.Actions.Controls;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.ContentPack;
using Robust.Shared.Utility;

namespace Content.Client.UserInterface.Systems.Actions;

/// <summary>
///     Alt-click a slot on the action bar, or drag it off the bar (or out of the actions menu) onto the screen, to set
///         its action loose as a floating button. Drag the button itself to move it while the action layout is
///         unlocked (see the arrange partial); right-click it to put it back on the bar; shift-click it to give its art
///         a key (see the rebind partial: the key belongs to the art, so it works the same on the bar or off it).
///         Floating buttons are remembered by the art's prototype and where they sat, so they come back loose after a
///         reconnect or the next round.
/// </summary>
public sealed partial class ActionUIController
{
    private static readonly ResPath FloatingActionsPath = new("/ks_action_floating.txt");

    /// <summary>
    ///     Half a button, to centre one on the cursor.
    /// </summary>
    private static readonly Vector2 FloatingActionHalfSize = new(32, 32);

    private readonly List<KsFloatingActionWindow> _floatingActions = new();

    /// <summary>
    ///     Art prototype to where its floating button sits, in UI units from the top left of the screen. Kept when a
    ///         button closes because the round ended or the item went away, so it comes back where it was.
    /// </summary>
    private readonly Dictionary<string, Vector2> _savedFloatingActions = new();
    private bool _savedFloatingActionsLoaded;

    /// <returns>True if this click popped the slot's action out.</returns>
    private bool TryFloatAction(ActionButton button)
    {
        if (_actionsSystem == null || button.Action is not { } action
            || IsFolderAction(button))
            return false;

        if (!KsCheckArrangeUnlocked())
            return true;

        if (FloatingWindowOf(action.Owner) == null)
            FloatActionAt(action.Owner, FloatingSpotBeside(button));

        return true;
    }

    /// <summary>
    ///     Takes an action off the bar (if it's there) and sets it loose at this spot, or moves its floating button
    ///         there if it already has one.
    /// </summary>
    private void FloatActionAt(EntityUid actionUid, Vector2 position)
    {
        if (_actionsSystem == null)
            return;

        if (FloatingWindowOf(actionUid) is { } existing)
        {
            MoveFloatingAction(existing, position);
            return;
        }

        var window = OpenFloatingAction(actionUid, position);
        RememberFloatingAction(window, ClampFloatingPosition(position));
        RemoveActionFromFolders(actionUid);
        SaveActionConfigurationAfterChange();
        RefreshActionBar();
    }

    private KsFloatingActionWindow OpenFloatingAction(EntityUid actionUid, Vector2 position)
    {
        var window = new KsFloatingActionWindow(EntityManager, _actionsSystem!, actionUid);
        window.OnPress += (args, w) => HandleActionPressed(args, w.Button);
        window.OnRelease += (args, w) => HandleActionUnpressed(args, w.Button);
        window.OnDock += OnFloatingDockPressed;
        window.OnBind += StartFloatingRebind;
        window.OnQuickCastToggle += w => ToggleQuickCast(w.ActionUid);
        UIManager.WindowRoot.AddChild(window);
        LayoutContainer.SetPosition(window, ClampFloatingPosition(position));
        _floatingActions.Add(window);
        return window;
    }

    private void MoveFloatingAction(KsFloatingActionWindow window, Vector2 position)
    {
        var clamped = ClampFloatingPosition(position);
        LayoutContainer.SetPosition(window, clamped);
        RememberFloatingAction(window, clamped);
    }

    /// <summary>
    ///     Keeps a whole button on the screen.
    /// </summary>
    private Vector2 ClampFloatingPosition(Vector2 position)
    {
        var max = Vector2.Max(Vector2.Zero, UIManager.WindowRoot.Size - FloatingActionHalfSize * 2);
        return Vector2.Clamp(position, Vector2.Zero, max);
    }

    private KsFloatingActionWindow? FloatingWindowOf(EntityUid actionUid)
    {
        return _floatingActions.FirstOrDefault(window => window.ActionUid == actionUid);
    }

    /// <summary>
    ///     The floating window an action button belongs to, if it's a floating one.
    /// </summary>
    private KsFloatingActionWindow? FloatingWindowOf(ActionButton button)
    {
        return _floatingActions.FirstOrDefault(window => window.Button == button);
    }

    private void OnFloatingDockPressed(KsFloatingActionWindow window)
    {
        if (!KsCheckArrangeUnlocked())
            return;

        DockFloatingAction(window);
    }

    private void DockFloatingAction(KsFloatingActionWindow window)
    {
        CloseFloatingAction(window, forget: true);
        if (_actionsSystem?.GetAction(window.ActionUid) is { } action && action.Comp.AttachedEntity != null)
        {
            AddActionToRoot(window.ActionUid);
            SaveActionConfigurationAfterChange();
            RefreshActionBar();
        }
    }

    /// <summary>
    ///     Closes a floating button.
    /// </summary>
    /// <param name="forget">
    ///     Whether the player put it somewhere else (onto the bar, into the bin), so it shouldn't come back loose
    ///         next time.
    /// </param>
    private void CloseFloatingAction(KsFloatingActionWindow window, bool forget)
    {
        if (_rebindingAction == window.ActionUid)
            CancelSlotRebind();

        _floatingActions.Remove(window);
        window.Close();
        if (forget && ProtoOf(window.ActionUid) is { } proto && _savedFloatingActions.Remove(proto))
            SaveFloatingActions();
    }

    /// <summary>
    ///     A floating button whose action is gone goes with it, but is remembered, so the art comes back loose if the
    ///         player gets it again.
    /// </summary>
    private void RemoveFloatingAction(EntityUid actionUid)
    {
        foreach (var window in _floatingActions.Where(window => window.ActionUid == actionUid).ToList())
            CloseFloatingAction(window, forget: false);
    }

    private void CloseFloatingActions()
    {
        foreach (var window in _floatingActions)
            window.Close();

        _floatingActions.Clear();
    }

    private void RememberFloatingAction(KsFloatingActionWindow window, Vector2 position)
    {
        if (ProtoOf(window.ActionUid) is not { } proto)
            return;

        EnsureFloatingActionsLoaded();
        _savedFloatingActions[proto] = position;
        SaveFloatingActions();
    }

    /// <summary>
    ///     Brings back the floating buttons the player had last time, for arts they have now, and keeps every
    ///         floating art off the bar (a saved layout or auto-populate can put one back on it).
    /// </summary>
    private void RestoreFloatingActions()
    {
        if (_actionsSystem == null)
            return;

        EnsureFloatingActionsLoaded();
        foreach (var (proto, position) in _savedFloatingActions)
        {
            if (!_actionLayoutPersistenceEnabled
                || _floatingActions.Any(window => ProtoOf(window.ActionUid) == proto)
                || FindAction(proto) is not { } actionUid)
                continue;

            OpenFloatingAction(actionUid, position);
        }

        foreach (var window in _floatingActions)
        {
            if (ContainsAssignedAction(window.ActionUid))
                RemoveActionFromFolders(window.ActionUid);
        }
    }

    private void EnsureFloatingActionsLoaded()
    {
        if (_savedFloatingActionsLoaded)
            return;

        _savedFloatingActionsLoaded = true;
        if (!_resourceManager.UserData.TryReadAllText(FloatingActionsPath, out var text))
            return;

        // One art per line: prototype, then x and y.
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split('|');
            if (parts.Length != 3
                || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                || !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                continue;

            _savedFloatingActions[parts[0]] = new Vector2(x, y);
        }
    }

    private void SaveFloatingActions()
    {
        if (_isLeavingServer)
            return;

        var text = new StringBuilder();
        foreach (var (proto, position) in _savedFloatingActions)
        {
            text.Append(proto).Append('|')
                .Append(position.X.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(position.Y.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        _resourceManager.UserData.WriteAllText(FloatingActionsPath, text.ToString());
    }
}
