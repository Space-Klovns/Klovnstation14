using Content.Client.UserInterface.Systems.Actions.Controls;
using Content.Shared.Input;
using Content.Shared.Popups;
using Robust.Client.Input;
using Robust.Shared.Input;

namespace Content.Client.UserInterface.Systems.Actions;

/// <summary>
///     Shift-click a slot on the action bar, then press a key (with Ctrl, Shift or Alt if you like): that key now uses
///         the slot. Escape cancels. The binding is saved like any other in the Controls options.
/// </summary>
public sealed partial class ActionUIController
{
    private BoundKeyFunction? _rebindingSlot;

    /// <returns>True if this click started rebinding the slot's key.</returns>
    private bool TryStartSlotRebind(ActionButton button)
    {
        if (!_input.IsKeyDown(Keyboard.Key.Shift) || _container == null || !_container.TryGetButtonIndex(button, out var index))
            return false;

        var hotbarKeys = ContentKeyFunctions.GetHotbarBoundKeys();
        if (index < 0 || index >= hotbarKeys.Length)
            return false;

        CancelSlotRebind();
        _rebindingSlot = hotbarKeys[index];
        _input.FirstChanceOnKeyEvent += OnRebindKey;
        Popup(Loc.GetString("ks-action-rebind-prompt", ("slot", index + 1)));
        return true;
    }

    private void CancelSlotRebind()
    {
        if (_rebindingSlot == null)
            return;

        _rebindingSlot = null;
        _input.FirstChanceOnKeyEvent -= OnRebindKey;
    }

    private void OnRebindKey(KeyEventArgs keyEvent, KeyEventType type)
    {
        if (_rebindingSlot is not { } function)
            return;

        var key = keyEvent.Key;
        // The click that started this, and the modifiers on their own, are not the key being bound.
        if (key is Keyboard.Key.MouseLeft or Keyboard.Key.MouseRight or Keyboard.Key.MouseMiddle
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

        var mods = new Keyboard.Key[3];
        var count = 0;
        if (keyEvent.Control)
            mods[count++] = Keyboard.Key.Control;
        if (keyEvent.Shift)
            mods[count++] = Keyboard.Key.Shift;
        if (keyEvent.Alt)
            mods[count++] = Keyboard.Key.Alt;

        // The slot gets exactly this key: whatever it was bound to before goes.
        foreach (var binding in new List<IKeyBinding>(_input.GetKeyBindings(function)))
            _input.RemoveBinding(binding);

        _input.RegisterBinding(new KeyBindingRegistration
        {
            Function = function,
            BaseKey = key,
            Mod1 = mods[0],
            Mod2 = mods[1],
            Mod3 = mods[2],
            Type = KeyBindingType.State,
            CanRepeat = false,
        });
        _input.SaveToUserData();
        RefreshActionBar();

        var keyName = _input.TryGetKeyBinding(function, out var newBinding) ? newBinding.GetKeyString() : key.ToString();
        Popup(Loc.GetString("ks-action-rebind-done", ("key", keyName)));
    }

    private void Popup(string message)
    {
        EntityManager.System<Content.Client.Popups.PopupSystem>().PopupCursor(message, PopupType.Small);
    }
}
