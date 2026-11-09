using Content.Shared.Containers.ItemSlots;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     The "Eject item" button of a slot that can be empty. tg disables it when the slot is empty, here it becomes
///         "Insert": pressing it tries to put the item in the player's active hand into the slot, as if used on the
///         machine. Set <see cref="Filled"/> from the window's state.
/// </summary>
/// <remarks>
///     With a <see cref="SlotId"/> (an <c>ItemSlotsComponent</c> slot) a press raises <see cref="SlotPressed"/> with the
///         message for the server, ejecting when filled and inserting when empty. Without one, for a slot that is
///         not an item slot (a lathe's material, a plain container), handle <see cref="EjectPressed"/> and
///         <see cref="InsertPressed"/> instead; they are raised in both forms.
/// </remarks>
public sealed class KsTguiItemSlotButton : KsTguiButton
{
    private bool _filled = true;
    private string? _filledText;
    private string? _emptyText;

    /// <summary>
    ///     The slot this button acts on. Set to get <see cref="SlotPressed"/>.
    /// </summary>
    public string? SlotId;

    /// <summary>
    ///     Raised with the message to send when <see cref="SlotId"/> is set.
    /// </summary>
    public event Action<ItemSlotButtonPressedEvent>? SlotPressed;

    public event Action? EjectPressed;
    public event Action? InsertPressed;

    public KsTguiItemSlotButton()
    {
        Refresh();
        OnPressed += _ => Press();
    }

    /// <summary>
    ///     Whether the slot holds an item: the eject icon and text when it does, the insert ones when it doesn't.
    /// </summary>
    public bool Filled
    {
        get => _filled;
        set
        {
            if (_filled == value)
                return;

            _filled = value;
            Refresh();
        }
    }

    /// <summary>
    ///     Text while filled, instead of the plain "Eject" (a PDA has several slots, so "Eject ID").
    /// </summary>
    public string? FilledText
    {
        get => _filledText;
        set
        {
            _filledText = value;
            Refresh();
        }
    }

    /// <summary>
    ///     Text while empty, instead of the plain "Insert".
    /// </summary>
    public string? EmptyText
    {
        get => _emptyText;
        set
        {
            _emptyText = value;
            Refresh();
        }
    }

    private void Refresh()
    {
        Icon = _filled ? KsTguiIcons.Eject : KsTguiIcons.SignIn;
        Text = _filled
            ? _filledText ?? Loc.GetString("ks-tgui-slot-eject")
            : _emptyText ?? Loc.GetString("ks-tgui-slot-insert");
    }

    private void Press()
    {
        if (SlotId != null)
            SlotPressed?.Invoke(new ItemSlotButtonPressedEvent(SlotId, tryEject: _filled, tryInsert: !_filled));

        if (_filled)
            EjectPressed?.Invoke();
        else
            InsertPressed?.Invoke();
    }
}
