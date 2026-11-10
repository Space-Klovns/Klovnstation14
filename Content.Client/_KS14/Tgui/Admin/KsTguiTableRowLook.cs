using Content.Client.Administration.UI.CustomControls;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.Tgui.Admin;

/// <summary>
///     Makes one row of an existing admin list look like a row of tgui's <c>Table</c>. It is an invisible child of the row,
///         so the stylesheet reaches it exactly when it reaches the row, including when the theme is toggled live.
///         Under tgui it swaps the row's code-set background (which no rule could override) for the theme's candystripe
///         (or nothing, on a header row), hides the vertical bars between columns and embolds a header's labels, as
///         <c>Table.Row header</c> does. Under Baron it puts back exactly what it took away.
/// </summary>
public sealed class KsTguiTableRowLook : Control
{
    private readonly PanelContainer _background;
    private readonly bool _striped;
    private readonly bool _header;
    private readonly IReadOnlyList<Control> _separators;
    private readonly IReadOnlyList<Label> _labels;

    private StyleBox? _stockBackground;

    /// <param name="background">The panel whose <see cref="PanelContainer.PanelOverride"/> the row sets.</param>
    /// <param name="striped">Whether the row is one of the dark (odd) rows. Ignored for a header.</param>
    /// <param name="header">Whether this is the header row, which is not striped and is bold.</param>
    /// <param name="separators">The vertical bars between columns, hidden under tgui.</param>
    /// <param name="labels">The labels a header embolds.</param>
    public KsTguiTableRowLook(PanelContainer background, bool striped, bool header, IReadOnlyList<Control> separators, IReadOnlyList<Label> labels)
    {
        _background = background;
        _striped = striped;
        _header = header;
        _separators = separators;
        _labels = labels;

        MouseFilter = MouseFilterMode.Ignore;
        AddStyleClass(KsTguiSheetlet.StyleClassCandystripe);
    }

    /// <summary>
    ///     Gives <paramref name="row"/> the look: finds its <see cref="VSeparator"/>s and <see cref="Label"/>s itself and adds
    ///         the look as a child. <paramref name="background"/> is the panel whose override the row sets.
    /// </summary>
    public static KsTguiTableRowLook Attach(Control row, PanelContainer background, bool striped, bool header)
    {
        var separators = new List<Control>();
        var labels = new List<Label>();
        Collect(row, separators, labels);

        var look = new KsTguiTableRowLook(background, striped, header, separators, labels);
        row.AddChild(look);
        return look;
    }

    private static void Collect(Control control, List<Control> separators, List<Label> labels)
    {
        foreach (var child in control.Children)
        {
            if (child is VSeparator)
                separators.Add(child);
            else if (child is Label label)
                labels.Add(label);
            else
                Collect(child, separators, labels);
        }
    }

    /// <summary>
    ///     Whether the row is currently drawn the tgui way.
    /// </summary>
    public bool Active { get; private set; }

    protected override void StylePropertiesChanged()
    {
        base.StylePropertiesChanged();

        var tgui = TryGetStyleProperty<Color>(KsTguiSheetlet.StylePropertyCandystripe, out var oddColor);
        if (tgui == Active)
            return;

        if (tgui)
        {
            // Taken once, on the way in, since the row's code set it before the first style pass.
            _stockBackground = _background.PanelOverride;

            Color? stripe = null;
            if (!_header)
            {
                if (_striped)
                    stripe = oddColor;
                else if (TryGetStyleProperty<Color>(KsTguiSheetlet.StylePropertyCandystripeEven, out var even) && even.A > 0f)
                    stripe = even;
            }

            _background.PanelOverride = stripe is { } color ? new StyleBoxFlat(color) : null;
        }
        else
        {
            _background.PanelOverride = _stockBackground;
        }

        Active = tgui;

        foreach (var separator in _separators)
            separator.Visible = !tgui;

        if (!_header)
            return;

        foreach (var label in _labels)
        {
            if (tgui)
                label.AddStyleClass(KsTguiSheetlet.StyleClassTableHeaderLabel);
            else
                label.RemoveStyleClass(KsTguiSheetlet.StyleClassTableHeaderLabel);
        }
    }
}
