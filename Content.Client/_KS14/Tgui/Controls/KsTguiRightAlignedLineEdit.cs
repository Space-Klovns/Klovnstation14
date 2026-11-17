using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     A <see cref="LineEdit"/> whose text is right-aligned, as tgui's number inputs (<c>text-align: right</c>). The engine's
///         can only start its text at the left edge of its content box, so the box's left margin is kept at the width less the
///         text and the right padding: the text then ends at the right padding, and the caret, selection and clicks, which the
///         engine works out from the same content box, agree with what is drawn. The margin follows the text, the font and the
///         size, so it is worked out when any of them change. A text wider than the room takes the plain padding and scrolls
///         as the engine's does.
/// </summary>
[Virtual]
public class KsTguiRightAlignedLineEdit : LineEdit
{
    private readonly KsTguiStyleBox _box;
    private readonly float _sidePadding;

    /// <param name="box">
    ///     The box this edit draws with, which this takes the left margin of. It is given as <c>StyleBoxOverride</c>.
    /// </param>
    /// <param name="sidePadding">The content margin kept on both sides at least, in virtual pixels.</param>
    public KsTguiRightAlignedLineEdit(KsTguiStyleBox box, float sidePadding)
    {
        _box = box;
        _sidePadding = sidePadding;

        StyleBoxOverride = box;
        box.SetContentMarginOverride(StyleBox.Margin.Horizontal, sidePadding);
        OnTextChanged += _ => UpdateAlignment();
    }

    /// <summary>
    ///     The left margin the text sits at, in virtual pixels.
    /// </summary>
    public float TextLeftMargin => _box.GetContentMargin(StyleBox.Margin.Left);

    /// <summary>
    ///     Works the margin out again. Called by the edit itself when its size, style or typed text change, and by whoever
    ///         changes the text without the event (<c>Text = ...</c> raises none).
    /// </summary>
    public void UpdateAlignment()
    {
        var font = TryGetStyleProperty<Font>("font", out var styled) ? styled : UserInterfaceManager.ThemeDefaults.DefaultFont;

        // An empty box shows its placeholder from the same edge, so it is the placeholder that sits at the right.
        var shown = string.IsNullOrEmpty(Text) && !(HidePlaceHolderOnFocus && HasKeyboardFocus()) && PlaceHolder != null ? PlaceHolder : Text;
        var textWidth = KsTguiFitText.StringWidth(font, shown, UIScale) / UIScale;

        // The extra pixel is the caret's, which sits after the last character.
        var margin = MathF.Max(_sidePadding, Size.X - _sidePadding - textWidth - 1f);
        if (MathF.Abs(margin - TextLeftMargin) < 0.001f)
            return;

        _box.SetContentMarginOverride(StyleBox.Margin.Left, margin);
    }

    protected override void Resized()
    {
        base.Resized();

        UpdateAlignment();
    }

    protected override void StylePropertiesChanged()
    {
        base.StylePropertiesChanged();

        UpdateAlignment();
    }

    protected override void KeyboardFocusEntered()
    {
        base.KeyboardFocusEntered();

        UpdateAlignment();
    }

    protected override void KeyboardFocusExited()
    {
        base.KeyboardFocusExited();

        UpdateAlignment();
    }
}
