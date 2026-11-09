using System.Numerics;
using System.Text;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     tgui-core's FitText: the largest font size, found by the binary search tgui runs, at which the text is no wider than
///         <see cref="TextMaxWidth"/>. It searches again whenever the text, the width or the limits change.
/// </summary>
public sealed partial class KsTguiFitText : Control
{
    [Dependency] private KsTguiFonts _fonts = default!;

    private readonly Label _label = new();

    private string? _text;
    private float _maxWidth;
    private float _maxFontSize = 12f;
    private float _acceptableDifference = 5f;
    private int _fontSize;

    public KsTguiFitText()
    {
        IoCManager.InjectDependencies(this);
        AddChild(_label);
        Refit();
    }

    public string? Text
    {
        get => _text;
        set
        {
            _text = value;
            _label.Text = value;
            Refit();
        }
    }

    /// <summary>
    ///     tgui's <c>maxWidth</c>, in pixels.
    /// </summary>
    public float TextMaxWidth
    {
        get => _maxWidth;
        set
        {
            _maxWidth = value;
            Refit();
        }
    }

    /// <summary>
    ///     tgui's <c>maxFontSize</c>, in pixels: where the search starts from.
    /// </summary>
    public float MaxFontSize
    {
        get => _maxFontSize;
        set
        {
            _maxFontSize = value;
            Refit();
        }
    }

    /// <summary>
    ///     tgui's <c>acceptableDifference</c> (5 by default): how far under <see cref="TextMaxWidth"/> a size may be and be taken.
    /// </summary>
    public float AcceptableDifference
    {
        get => _acceptableDifference;
        set
        {
            _acceptableDifference = value;
            Refit();
        }
    }

    /// <summary>
    ///     The size, in pixels, the text is drawn at.
    /// </summary>
    public int FitFontSize => _fontSize;

    public Label Label => _label;

    /// <summary>
    ///     Searches again, as tgui does on a new text or window size.
    /// </summary>
    public void Refit()
    {
        _fontSize = Fit(size => MeasureWidth(_text, size), _maxWidth, _maxFontSize, _acceptableDifference);

        // A text at font-size 0 takes no room and shows nothing.
        _label.Visible = _fontSize > 0;
        if (_fontSize > 0)
            _label.FontOverride = _fonts.GetFont(_fontSize);

        InvalidateMeasure();
    }

    /// <summary>
    ///     tgui-core's <c>FitText.resize</c>, step for step: ten rounds of halving between 0 and <paramref name="maxFontSize"/>
    ///         by the width at the midpoint size. A width over the limit lowers the top, one more than
    ///         <paramref name="acceptableDifference"/> under it raises the bottom, and one in between stops the search. The size
    ///         is then rounded from the bounds as they stand, not from the size last tried, as tgui does.
    /// </summary>
    public static int Fit(Func<int, float> widthAtSize, float maxWidth, float maxFontSize, float acceptableDifference)
    {
        var low = 0f;
        var high = maxFontSize;
        for (var round = 0; round < 10; round++)
        {
            var size = JsRound((low + high) / 2f);
            var difference = widthAtSize((int)size) - maxWidth;
            if (difference > 0f)
                high = size;
            else if (difference < acceptableDifference)
                low = size;
            else
                break;
        }

        return (int)JsRound((low + high) / 2f);
    }

    /// <summary>
    ///     JavaScript's <c>Math.round</c>: halves go up.
    /// </summary>
    private static float JsRound(float value)
    {
        return MathF.Floor(value + 0.5f);
    }

    private float MeasureWidth(string? text, int size)
    {
        if (size <= 0 || string.IsNullOrEmpty(text))
            return 0f;

        return StringWidth(_fonts.GetFont(size), text, scale: 1f);
    }

    /// <summary>
    ///     How wide <paramref name="text"/> is in <paramref name="font"/>, in pixels, the way a Label measures it.
    /// </summary>
    public static float StringWidth(Font font, string text, float scale)
    {
        var width = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (font.TryGetCharMetrics(rune, scale, out var metrics))
                width += metrics.Advance;
        }

        return (float)width;
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        _label.Measure(new Vector2(float.PositiveInfinity, availableSize.Y));
        return _label.DesiredSize;
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        _label.Arrange(UIBox2.FromDimensions(Vector2.Zero, _label.DesiredSize));
        return finalSize;
    }
}
