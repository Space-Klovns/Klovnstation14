using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     tgui's <c>Button.Input</c>: shows <see cref="Value"/> (or <see cref="ButtonText"/>); pressing it swaps in a text
///         field with the text selected. Enter, or losing focus with a changed value, raises <see cref="Committed"/>;
///         Escape puts the old value back.
/// </summary>
public sealed class KsTguiInputButton : KsTguiButton
{
    private readonly LineEdit _edit;
    private string _value = string.Empty;
    private string? _buttonText;
    private bool _editing;
    private bool _cancelled;

    public KsTguiInputButton()
    {
        _edit = new LineEdit
        {
            Visible = false,
            StyleBoxOverride = new StyleBoxEmpty(),
            SelectAllOnFocus = true,
        };

        AddChild(_edit);
        _edit.OnTextEntered += _ => _edit.ReleaseKeyboardFocus();
        _edit.OnKeyBindDown += args =>
        {
            if (args.Function == EngineKeyFunctions.TextReleaseFocus)
                _cancelled = true;
        };
        _edit.OnFocusExit += _ => EndEdit();
        OnPressed += _ => BeginEdit();
        RefreshText();
    }

    /// <summary>
    ///     Raised with the new text when the player finishes editing a changed value.
    /// </summary>
    public event Action<string>? Committed;

    public bool Editing => _editing;

    public string Value
    {
        get => _value;
        set
        {
            _value = value;
            if (!_editing)
                RefreshText();
        }
    }

    /// <summary>
    ///     Shown instead of <see cref="Value"/> when set.
    /// </summary>
    public string? ButtonText
    {
        get => _buttonText;
        set
        {
            _buttonText = value;
            RefreshText();
        }
    }

    /// <summary>
    ///     The text being typed, for tests; null when not editing.
    /// </summary>
    public string? EditText => _editing ? _edit.Text : null;

    /// <summary>
    ///     Types into the field, as the player would.
    /// </summary>
    public void SetEditText(string text)
    {
        _edit.Text = text;
    }

    /// <summary>
    ///     Ends editing as Enter does, or as Escape when cancelled.
    /// </summary>
    public void Finish(bool cancel)
    {
        _cancelled = cancel;
        EndEdit();
    }

    private void RefreshText()
    {
        Text = _buttonText ?? _value;
    }

    /// <summary>
    ///     What a press does: swaps the text field in.
    /// </summary>
    public void BeginEdit()
    {
        if (_editing || Disabled)
            return;

        _editing = true;
        _cancelled = false;
        _edit.Text = _value;
        _edit.Visible = true;
        Label.FontColorOverride = Robust.Shared.Maths.Color.Black.WithAlpha(0f);
        _edit.GrabKeyboardFocus();
    }

    private void EndEdit()
    {
        if (!_editing)
            return;

        _editing = false;
        _edit.Visible = false;
        Label.FontColorOverride = null;

        var typed = _edit.Text;
        if (!_cancelled && typed != _value)
        {
            _value = typed;
            RefreshText();
            Committed?.Invoke(typed);
        }

        RefreshText();
    }

    protected override void ArrangeContent(UIBox2 textBox)
    {
        _edit.Measure(textBox.Size);
        _edit.Arrange(textBox);
    }
}
