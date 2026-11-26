using System.Linq;
using System.Text;
using Content.Client._KS14.Actions.UI;
using Content.Client.UserInterface.Systems.Actions.Controls;
using Content.Shared.Actions.Components;
using Content.Shared.Input;
using Content.Shared.Popups;
using Robust.Client.Input;
using Robust.Shared.ContentPack;
using Robust.Shared.Input;
using Robust.Shared.Utility;

namespace Content.Client.UserInterface.Systems.Actions;

/// <summary>
///     A key (and the modifiers held with it).
/// </summary>
public readonly record struct KsActionKey(Keyboard.Key Key, bool Control, bool Shift, bool Alt);

/// <summary>
///     Keys belong to arts, not to slots. Shift-click an art on the bar (or a floating button), then press a key (with
///         Ctrl, Shift or Alt if you like): that key now uses that art wherever its button sits, however the bar is
///         reordered, and the key is shown on its icon. Pressing the art's own key again clears it; a key given to one
///         art leaves any other. Escape cancels. Keys are saved by the art's prototype, so they last between rounds.
///         The bar's ordinary slot keys (1 to 0) still work on whatever sits in a slot, unless an art has claimed that
///         key. Mouse buttons other than left and right (the middle and side buttons) can be bound too. Ctrl-click an
///         aimed art to set it to quick-cast (outlined in blue): its key then casts at the cursor without the second
///         click, falling back to aiming if there's nothing under the cursor to cast it on.
/// </summary>
public sealed partial class ActionUIController
{
    private static readonly ResPath ActionKeysPath = new("/ks_action_keys.txt");
    private static readonly ResPath QuickCastPath = new("/ks_action_quickcast.txt");

    /// <summary>
    ///     Arts (by prototype) whose key casts at the cursor straight away instead of starting to aim.
    /// </summary>
    private readonly HashSet<string> _quickCast = new();


    /// <summary>
    ///     Art prototype to its key.
    /// </summary>
    private readonly Dictionary<string, KsActionKey> _actionKeys = new();
    private bool _actionKeysLoaded;
    private EntityUid? _rebindingAction;

    /// <returns>True if this click started binding a key to the slot's art.</returns>
    private bool TryStartSlotRebind(ActionButton button)
    {
        if (button.Action is not { } action || IsFolderAction(button))
            return false;

        StartActionRebind(action.Owner);
        return true;
    }

    private void StartFloatingRebind(KsFloatingActionWindow window)
    {
        StartActionRebind(window.ActionUid);
    }

    private void StartActionRebind(EntityUid actionUid)
    {
        // Shift-click comes through as more than one function; the second is the same request.
        if (_rebindingAction == actionUid)
            return;

        CancelSlotRebind();
        _rebindingAction = actionUid;
        _input.FirstChanceOnKeyEvent += OnRebindKey;
        Popup(Loc.GetString("ks-action-rebind-prompt", ("art", EntityManager.GetComponentOrNull<MetaDataComponent>(actionUid)?.EntityName ?? "")));
    }

    private bool Rebinding => _rebindingAction != null;

    private void CancelSlotRebind()
    {
        if (!Rebinding)
            return;

        _rebindingAction = null;
        _input.FirstChanceOnKeyEvent -= OnRebindKey;
    }

    private void OnRebindKey(KeyEventArgs keyEvent, KeyEventType type)
    {
        if (_rebindingAction is not { } actionUid)
            return;

        var key = keyEvent.Key;
        // The click that started this, and the modifiers on their own, are not the key being bound. Left and right
        //     click stay clicks; the middle button and the side buttons (4 to 9) can be bound like any key.
        if (key is Keyboard.Key.MouseLeft or Keyboard.Key.MouseRight
            or Keyboard.Key.Shift or Keyboard.Key.Control or Keyboard.Key.Alt or Keyboard.Key.LSystem or Keyboard.Key.RSystem)
            return;

        keyEvent.Handle();
        if (type != KeyEventType.Up)
            return;

        CancelSlotRebind();
        if (key == Keyboard.Key.Escape)
        {
            Popup(Loc.GetString("ks-action-rebind-cancelled"));
            return;
        }

        if (ProtoOf(actionUid) is not { } proto)
            return;

        var combo = new KsActionKey(key, keyEvent.Control, keyEvent.Shift, keyEvent.Alt);
        if (key is Keyboard.Key.BackSpace or Keyboard.Key.Delete)
        {
            // Back to the default: no key of its own, so the slot it sits in uses its ordinary number key again.
            _actionKeys.Remove(proto);
            Popup(Loc.GetString("ks-action-rebind-reset"));
        }
        else if (_actionKeys.TryGetValue(proto, out var current) && current == combo)
        {
            // Its own key again: clear it.
            _actionKeys.Remove(proto);
            Popup(Loc.GetString("ks-action-rebind-cleared"));
        }
        else
        {
            // A key belongs to one art at a time.
            foreach (var (otherProto, otherKey) in _actionKeys.ToList())
            {
                if (otherKey == combo)
                    _actionKeys.Remove(otherProto);
            }

            _actionKeys[proto] = combo;
            Popup(Loc.GetString("ks-action-rebind-done", ("key", KeyName(combo))));
        }

        SaveActionKeys();
        RefreshSlotKeyLabels();
    }

    /// <summary>
    ///     An art's key uses it, before the key does anything else (unless you're typing somewhere).
    /// </summary>
    private void OnActionKey(KeyEventArgs keyEvent, KeyEventType type)
    {
        if (Rebinding || UIManager.KeyboardFocused != null || _actionsSystem == null || _actionKeys.Count == 0)
            return;

        var pressed = new KsActionKey(keyEvent.Key, keyEvent.Control, keyEvent.Shift, keyEvent.Alt);
        foreach (var (proto, combo) in _actionKeys)
        {
            if (combo != pressed)
                continue;

            // Only if they have that art right now; otherwise the key does whatever it normally does.
            if (FindAction(proto) is not { } actionUid)
                return;

            keyEvent.Handle();
            if (type == KeyEventType.Down && !keyEvent.IsRepeat)
            {
                if (_quickCast.Contains(proto))
                    KsQuickCastAction(actionUid);
                else
                    KsUseAction(actionUid);
            }
            return;
        }
    }

    /// <returns>True if this click toggled quick-cast on the slot's art.</returns>
    private bool TryToggleQuickCast(ActionButton button)
    {
        if (button.Action is not { } action || IsFolderAction(button))
            return false;

        ToggleQuickCast(action.Owner);
        return true;
    }

    private void ToggleQuickCast(EntityUid actionUid)
    {
        EnsureActionKeys();
        if (ProtoOf(actionUid) is not { } proto)
            return;

        // Only aimed arts have anything to skip.
        if (!EntityManager.HasComponent<TargetActionComponent>(actionUid))
        {
            Popup(Loc.GetString("ks-action-quickcast-not-aimed"));
            return;
        }

        var on = _quickCast.Add(proto);
        if (!on)
            _quickCast.Remove(proto);

        Popup(Loc.GetString(on ? "ks-action-quickcast-on" : "ks-action-quickcast-off"));
        SaveActionKeys();
        RefreshSlotKeyLabels();
    }

    /// <summary>
    ///     Cast an art at the cursor at once; if there's nothing there to cast it on, aim it the usual way instead.
    /// </summary>
    private void KsQuickCastAction(EntityUid actionUid)
    {
        if (_actionsSystem?.GetAction(actionUid) is not { } action)
            return;

        var eyeManager = IoCManager.Resolve<Robust.Client.Graphics.IEyeManager>();
        var mouseScreen = _input.MouseScreenPosition;
        var mouse = mouseScreen.Window == Robust.Shared.Map.WindowId.Invalid
            ? Robust.Shared.Map.MapCoordinates.Nullspace
            : eyeManager.PixelToMap(mouseScreen);
        var underMouse = IoCManager.Resolve<Robust.Client.State.IStateManager>().CurrentState is Content.Client.Gameplay.GameplayStateBase state
            ? state.GetClickedEntity(mouse)
            : null;

        switch (_actionsSystem.KsQuickCast(action, mouse, underMouse))
        {
            case Content.Client.Actions.ActionsSystem.KsQuickCastResult.TooFar:
                Popup(Loc.GetString("ks-action-quickcast-too-far"));
                break;
            case Content.Client.Actions.ActionsSystem.KsQuickCastResult.NeedsAim:
                KsUseAction(actionUid);
                break;
        }
    }

    private EntityUid? FindAction(string proto)
    {
        if (_actionsSystem == null)
            return null;

        foreach (var action in _actionsSystem.GetClientActions())
        {
            if (ProtoOf(action.Owner) == proto)
                return action.Owner;
        }

        return null;
    }

    private string? ProtoOf(EntityUid actionUid)
    {
        return EntityManager.GetComponentOrNull<MetaDataComponent>(actionUid)?.EntityPrototype?.ID;
    }

    /// <summary>
    ///     The key this art has, if any.
    /// </summary>
    private KsActionKey? KeyOf(EntityUid actionUid)
    {
        return ProtoOf(actionUid) is { } proto && _actionKeys.TryGetValue(proto, out var combo) ? combo : null;
    }

    private string KeyName(KsActionKey combo)
    {
        var name = _input.GetKeyName(combo.Key);
        if (combo.Alt)
            name = "Alt+" + name;
        if (combo.Shift)
            name = "Shift+" + name;
        if (combo.Control)
            name = "Ctrl+" + name;
        return name;
    }

    private static KsActionKey ComboOf(IKeyBinding binding)
    {
        bool Has(Keyboard.Key mod) => binding.Mod1 == mod || binding.Mod2 == mod || binding.Mod3 == mod;
        return new KsActionKey(binding.BaseKey, Has(Keyboard.Key.Control), Has(Keyboard.Key.Shift), Has(Keyboard.Key.Alt));
    }

    /// <summary>
    ///     Every button shows the key that uses it: its art's own key, or else the slot's ordinary key if no art has
    ///         claimed that one. Floating buttons show their art's key.
    /// </summary>
    private void RefreshSlotKeyLabels()
    {
        EnsureActionKeys();
        var claimed = new HashSet<KsActionKey>(_actionKeys.Values);
        if (_container != null)
        {
            var hotbarKeys = ContentKeyFunctions.GetHotbarBoundKeys();
            var index = 0;
            foreach (var child in _container.Children)
            {
                if (child is not ActionButton button)
                    continue;

                var text = string.Empty;
                if (button.Action is { } action && KeyOf(action.Owner) is { } own)
                    text = KeyName(own);
                else if (index < hotbarKeys.Length && _input.TryGetKeyBinding(hotbarKeys[index], out var binding)
                         && !claimed.Contains(ComboOf(binding)))
                    text = binding.GetKeyString();

                button.Label.Text = text;
                button.SetKsQuickCast(button.Action is { } shown && ProtoOf(shown.Owner) is { } shownProto && _quickCast.Contains(shownProto));
                index++;
            }
        }

        foreach (var window in _floatingActions)
        {
            window.SetKeyLabel(KeyOf(window.ActionUid) is { } key ? KeyName(key) : string.Empty);
            window.Button.SetKsQuickCast(ProtoOf(window.ActionUid) is { } floatingProto && _quickCast.Contains(floatingProto));
        }
    }

    private void EnsureActionKeys()
    {
        if (_actionKeysLoaded)
            return;

        _actionKeysLoaded = true;
        _input.FirstChanceOnKeyEvent += OnActionKey;
        if (_resourceManager.UserData.TryReadAllText(QuickCastPath, out var quickText))
        {
            foreach (var line in quickText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                _quickCast.Add(line);
        }

        if (!_resourceManager.UserData.TryReadAllText(ActionKeysPath, out var text))
            return;

        // One art per line: prototype, key, then ctrl/shift/alt as 0 or 1.
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split('|');
            if (parts.Length != 5 || !Enum.TryParse<Keyboard.Key>(parts[1], out var key))
                continue;

            _actionKeys[parts[0]] = new KsActionKey(key, parts[2] == "1", parts[3] == "1", parts[4] == "1");
        }
    }

    private void SaveActionKeys()
    {
        var text = new StringBuilder();
        foreach (var (proto, combo) in _actionKeys)
            text.Append($"{proto}|{combo.Key}|{(combo.Control ? 1 : 0)}|{(combo.Shift ? 1 : 0)}|{(combo.Alt ? 1 : 0)}\n");

        _resourceManager.UserData.WriteAllText(ActionKeysPath, text.ToString());
        _resourceManager.UserData.WriteAllText(QuickCastPath, string.Join("\n", _quickCast));
    }

    private void Popup(string message)
    {
        EntityManager.System<Content.Client.Popups.PopupSystem>().PopupCursor(message, PopupType.Small);
    }
}
