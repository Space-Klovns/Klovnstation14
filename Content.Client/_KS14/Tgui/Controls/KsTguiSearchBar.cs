using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     tg's <c>SearchBar</c> (<c>interfaces/common/SearchBar.tsx</c>): a magnifier, then an input taking the rest of the row,
///         with a Stack's gap between.
/// </summary>
public sealed class KsTguiSearchBar : BoxContainer
{
    public KsTguiSearchBar()
    {
        Orientation = LayoutOrientation.Horizontal;
        SeparationOverride = 6;
        HorizontalExpand = true;

        Icon = new KsTguiIcon { Glyph = KsTguiIcons.Search, VerticalAlignment = VAlignment.Center };
        Input = new LineEdit { HorizontalExpand = true };

        AddChild(Icon);
        AddChild(Input);
    }

    public KsTguiIcon Icon { get; }

    public LineEdit Input { get; }

    /// <summary>
    ///     tg's <c>noIcon</c>, inverted.
    /// </summary>
    public bool ShowIcon
    {
        get => Icon.Visible;
        set => Icon.Visible = value;
    }

    public string? PlaceHolder
    {
        get => Input.PlaceHolder;
        set => Input.PlaceHolder = value;
    }
}
