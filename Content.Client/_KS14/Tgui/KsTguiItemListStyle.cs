using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.Tgui;

/// <summary>
///     The colours of tgui's list row: a transparent <c>candystripe</c> Button, as <c>SpawnSearch.tsx</c> lays them out.
/// </summary>
/// <param name="Stripe"><c>--candystripe-odd</c>, over the first, third and so on row.</param>
/// <param name="Hover">A hovered transparent Button's wash: its text colour at .1.</param>
/// <param name="SelectedHover">The same wash on a selected one: the selected green at .1, a step lighter.</param>
/// <param name="DisabledWash"><c>--color-bad</c> at .2.</param>
/// <param name="Text"><c>--button-color-transparent</c>: the text colour at .5.</param>
/// <param name="TextHover">The same, at 1.</param>
/// <param name="TextSelected"><c>--button-background-selected</c> at +<c>--adjust-color</c> lightness.</param>
/// <param name="TextDisabled"><c>--color-bad</c>.</param>
public sealed record KsTguiListColors(
    Color Stripe,
    Color Hover,
    Color SelectedHover,
    Color DisabledWash,
    Color Text,
    Color TextHover,
    Color TextSelected,
    Color TextDisabled);

/// <summary>
///     What one <see cref="ItemList"/> row looks like, worked out from its state alone.
/// </summary>
/// <param name="Striped">Whether the row is one of the dark ones (CSS <c>nth-child(odd)</c>, so the first).</param>
/// <param name="Wash">The background colour behind the stripe, or none.</param>
/// <param name="Text">The text colour.</param>
public readonly record struct KsTguiItemRowLook(bool Striped, Color? Wash, Color Text);

/// <summary>
///     Where the tgui sheet learns which <see cref="ItemList"/>s exist. An <see cref="ItemList"/> draws every row with one
///         shared stylebox and one font colour, so the stripe by index, a hovered row and the selected row's green text can
///         only be drawn by something that can tell which list and which row it is drawing. The engine tells a stylebox
///         nothing but a rectangle, so it is told which lists exist by this selector (a rule that matches every list in a
///         tgui-styled tree, and so also records it), and finds the row by the rectangle the list has just given its item.
/// </summary>
/// <remarks>
///     Weakly held, and only ever looked at while a row draws, so a list that goes away costs nothing.
/// </remarks>
public static class KsTguiItemListRows
{
    private sealed class Entry(ItemList list)
    {
        public readonly WeakReference<ItemList> List = new(list);
        public int Hint;
    }

    private static readonly List<Entry> Entries = new();

    /// <summary>
    ///     Called while a list is styled. Idempotent.
    /// </summary>
    public static void Register(ItemList list)
    {
        for (var i = Entries.Count - 1; i >= 0; i--)
        {
            if (!Entries[i].List.TryGetTarget(out var other))
            {
                Entries.RemoveAt(i);
                continue;
            }

            if (other == list)
                return;
        }

        Entries.Add(new Entry(list));
    }

    /// <summary>
    ///     Finds the list and row that were just given <paramref name="region"/>, preferring the list that is drawn at
    ///         <paramref name="drawPosition"/> when several lists have a row of the same rectangle.
    /// </summary>
    public static bool TryFindRow(UIBox2 region, Vector2 drawPosition, out ItemList list, out int index)
    {
        list = null!;
        index = -1;
        var found = false;

        for (var i = Entries.Count - 1; i >= 0; i--)
        {
            var entry = Entries[i];
            if (!entry.List.TryGetTarget(out var candidate))
            {
                Entries.RemoveAt(i);
                continue;
            }

            if (!candidate.IsInsideTree)
                continue;

            var candidateIndex = FindIndex(entry, candidate, region);
            if (candidateIndex < 0)
                continue;

            var atPosition = (Vector2)candidate.GlobalPixelPosition == drawPosition;
            if (found && !atPosition)
                continue;

            list = candidate;
            index = candidateIndex;
            found = true;

            if (atPosition)
                break;
        }

        return found;
    }

    private static int FindIndex(Entry entry, ItemList list, UIBox2 region)
    {
        var count = list.Count;

        // Rows draw one after the other, so it is usually the one after the last.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var hint = entry.Hint + attempt;
            if (hint < count && list[hint].Region == region)
            {
                entry.Hint = hint;
                return hint;
            }
        }

        for (var i = 0; i < count; i++)
        {
            if (list[i].Region != region)
                continue;

            entry.Hint = i;
            return i;
        }

        return -1;
    }

    /// <summary>
    ///     How a row looks. Pure, so that a test can ask without a draw.
    /// </summary>
    public static KsTguiItemRowLook Look(KsTguiListColors colors, int index, bool selected, bool disabled, bool hovered)
    {
        var striped = index % 2 == 0;

        // tgui's disabled Button: --color-bad on a --color-bad wash, with no hover of its own.
        if (disabled)
            return new KsTguiItemRowLook(striped, colors.DisabledWash, colors.TextDisabled);

        // Selected stays clear, as Button.Checkbox does; only its text is green.
        if (selected)
            return new KsTguiItemRowLook(striped, hovered ? colors.SelectedHover : null, colors.TextSelected);

        return new KsTguiItemRowLook(striped, hovered ? colors.Hover : null, hovered ? colors.TextHover : colors.Text);
    }

    /// <summary>
    ///     The look of one row of a list in its current state, with the mouse supplied by the caller.
    /// </summary>
    public static KsTguiItemRowLook Look(KsTguiListColors colors, ItemList list, int index, bool hovered)
    {
        var item = list[index];
        var clickable = item.Selectable && list.SelectMode != ItemList.ItemListSelectMode.None;
        return Look(colors, index, item.Selected, item.Disabled, hovered && (clickable || item.Disabled));
    }

    /// <summary>
    ///     Whether the mouse is over a row. The row's rectangle is in the list's own pixels.
    /// </summary>
    public static bool IsHovered(IUserInterfaceManager userInterface, ItemList list, ItemList.Item item)
    {
        if (userInterface.CurrentlyHovered != list || item.Region is not { } region)
            return false;

        var local = (userInterface.MousePositionScaled.Position - list.GlobalPosition) * list.UIScale;
        return region.Contains(local);
    }
}

/// <summary>
///     A rule that matches every <see cref="ItemList"/> and registers it with <see cref="KsTguiItemListRows"/>. Where a rule
///         with the same selector would have no way of being told which list it matched.
/// </summary>
public sealed class KsTguiItemListSelector : Selector
{
    private readonly SelectorElement _list = SelectorElement.Type(typeof(ItemList));

    public override bool Matches(Control control)
    {
        if (!_list.Matches(control))
            return false;

        KsTguiItemListRows.Register((ItemList)control);
        return true;
    }

    public override StyleSpecificity CalculateSpecificity()
    {
        return _list.CalculateSpecificity();
    }
}

/// <summary>
///     The row of a tgui-styled <see cref="ItemList"/>: tgui's <c>candystripe</c> transparent Button, 20 tall with a 2px
///         margin under it (<c>--button-height</c>, <c>--space-xs</c>), 6px padding at the sides and a 2px radius. The
///         engine gives it a rectangle and nothing else, so the row is found from it (<see cref="KsTguiItemListRows"/>) and
///         the box paints its background, hover, stripe and text itself. The list's own text is made invisible by the
///         sheet (an <see cref="ItemList"/> has one font colour for all rows), and this box's is drawn in its place.
/// </summary>
public sealed class KsTguiItemListRowStyleBox : StyleBox
{
    /// <summary>
    ///     <c>--button-height</c>.
    /// </summary>
    public const float RowHeight = 20f;

    /// <summary>
    ///     The Button's <c>margin-bottom</c>, <c>--space-xs</c>: the room below each row that the stripe does not cover.
    /// </summary>
    public const float RowGap = 2f;

    /// <summary>
    ///     <c>--space-m</c>.
    /// </summary>
    public const float RowPadding = 6f;

    /// <summary>
    ///     <c>--button-border-radius</c>.
    /// </summary>
    public const float RowRadius = 2f;

    private readonly KsTguiListColors _colors;
    private readonly KsTguiStyleBox _wash = new(Color.White, RowRadius);
    private readonly KsTguiStyleBox _stripe = new(Color.White, RowRadius);

    /// <param name="colors">What the row is painted in.</param>
    /// <param name="fontHeight">The height of the list's font at scale 1, which the margins put the text in the middle of.</param>
    public KsTguiItemListRowStyleBox(KsTguiListColors colors, float fontHeight)
    {
        _colors = colors;

        var vertical = MathF.Max((RowHeight - fontHeight) / 2f, 0f);
        ContentMarginTopOverride = vertical;
        ContentMarginBottomOverride = vertical + RowGap;
        ContentMarginLeftOverride = RowPadding;
        ContentMarginRightOverride = RowPadding;
    }

    public KsTguiListColors Colors => _colors;

    protected override void DoDraw(DrawingHandleScreen handle, UIBox2 box, float uiScale)
    {
        var userInterface = IoCManager.Resolve<IUserInterfaceManager>();
        var transform = handle.GetTransform();
        if (!KsTguiItemListRows.TryFindRow(box, new Vector2(transform.M31, transform.M32), out var list, out var index))
            return;

        var item = list[index];
        var hovered = KsTguiItemListRows.IsHovered(userInterface, list, item);
        var look = KsTguiItemListRows.Look(_colors, list, index, hovered);

        var rowBox = new UIBox2(box.Left, box.Top, box.Right, MathF.Max(box.Top, box.Bottom - RowGap * uiScale));

        if (look.Wash is { } wash)
        {
            _wash.BackgroundColor = wash;
            _wash.Draw(handle, rowBox, uiScale);
        }

        // CSS paints the stripe, a background image, above the background colour.
        if (look.Striped)
        {
            _stripe.BackgroundColor = _colors.Stripe;
            _stripe.Draw(handle, rowBox, uiScale);
        }

        DrawText(handle, list, item, GetContentBox(box, uiScale), uiScale, look.Text);
    }

    private static void DrawText(DrawingHandleScreen handle, ItemList list, ItemList.Item item, UIBox2 contentBox, float uiScale, Color color)
    {
        if (item.Text == null)
            return;

        var font = list.ActualFont;
        var textStart = MathF.Min(contentBox.Left + item.IconSize.X * item.IconScale, contentBox.Right);
        var textBox = new UIBox2(textStart, contentBox.Top, contentBox.Right, contentBox.Bottom);
        var offsetY = (int)(textBox.Height - font.GetHeight(uiScale)) / 2;
        var baseLine = new Vector2(textBox.Left, textBox.Top + offsetY + font.GetAscent(uiScale));

        foreach (var rune in item.Text.EnumerateRunes())
        {
            if (!font.TryGetCharMetrics(rune, uiScale, out var metrics))
                continue;

            if (!(baseLine.X < textBox.Left || baseLine.X + metrics.Advance > textBox.Right))
                font.DrawChar(handle, rune, baseLine, uiScale, color);

            baseLine += new Vector2(metrics.Advance, 0f);
        }
    }
}
