using System.Numerics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.Input;
using Robust.Shared;
using Robust.Shared.Timing;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     One row of a <see cref="KsTguiList"/>: tgui's <c>&lt;Button color="transparent" fluid&gt;</c> with no transition, as
///         <c>SpawnSearch.tsx</c> sets on its rows.
/// </summary>
public sealed class KsTguiListRow : KsTguiButton
{
    public KsTguiListRow()
    {
        Color = KsTguiColor.Transparent;
        Fluid = true;
        Instant = true;
    }

    /// <summary>
    ///     What the row stands for, for the caller to read back from a selection.
    /// </summary>
    public object? Metadata { get; set; }
}

/// <summary>
///     The standard list of the tgui theme: tgui's <c>VirtualList</c> of
///         <c>&lt;Button className="candystripe" color="transparent" fluid selected={...}&gt;</c>, as
///         <c>SpawnSearch.tsx</c> builds it. Single selection; a click selects (<see cref="OnSelected"/>), a second click on
///         the same row soon after, or Enter, activates it (<see cref="OnActivated"/>), and Up and Down move the selection
///         while the list has the keyboard. Put it in a <see cref="KsTguiSection"/> with <c>Scrollable</c> as tgui does;
///         moving the selection scrolls it into view.
/// </summary>
/// <remarks>
///     <para>
///         The engine's own lists follow the same look under the tgui theme (<c>ItemList</c>, and the rows of
///             <c>ListContainer</c> that carry <c>ListContainer.StyleClassListContainerButton</c>), so a window that keeps
///             one of them needs nothing. Use this for a list a ported window builds itself.
///     </para>
///     <para>
///         Nothing here is virtualised: every row is a control. For thousands of rows use a <c>ListContainer</c>.
///     </para>
/// </remarks>
public sealed class KsTguiList : KsTguiCandystripe
{
    private readonly List<KsTguiListRow> _rows = new();

    private int? _selectedIndex;
    private int _lastPressedIndex = -1;
    private TimeSpan _lastPressedTime;
    private Vector2 _lastPressedPosition;

    public KsTguiList()
    {
        CanKeyboardFocus = true;
    }

    /// <summary>
    ///     Raised when the player selects a row, with its index. Not raised by <see cref="Select"/>.
    /// </summary>
    public event Action<int>? OnSelected;

    /// <summary>
    ///     Raised when the player activates a row: a double click, or Enter on the selected row. tgui's
    ///         <c>onDoubleClick</c>.
    /// </summary>
    public event Action<int>? OnActivated;

    public int Count => _rows.Count;

    public IReadOnlyList<KsTguiListRow> Rows => _rows;

    public int? SelectedIndex => _selectedIndex;

    public KsTguiListRow? SelectedRow => _selectedIndex is { } index ? _rows[index] : null;

    /// <summary>
    ///     Adds a row at the end.
    /// </summary>
    public KsTguiListRow AddRow(string text, object? metadata = null)
    {
        var row = new KsTguiListRow { Text = text, Metadata = metadata };
        _rows.Add(row);
        AddChild(row);

        row.OnPressed += args => OnRowPressed(row, args);
        return row;
    }

    /// <summary>
    ///     Removes every row, and the selection.
    /// </summary>
    public void Clear()
    {
        _rows.Clear();
        _selectedIndex = null;
        _lastPressedIndex = -1;
        RemoveAllChildren();
    }

    /// <summary>
    ///     Selects a row without raising <see cref="OnSelected"/>. Null, or an index with no row, clears the selection.
    /// </summary>
    public void Select(int? index)
    {
        if (index is { } value && (value < 0 || value >= _rows.Count))
            index = null;

        _selectedIndex = index;

        for (var i = 0; i < _rows.Count; i++)
            _rows[i].Selected = i == index;
    }

    private void OnRowPressed(KsTguiListRow row, BaseButton.ButtonEventArgs args)
    {
        PressRow(_rows.IndexOf(row), args.Event.PointerLocation.Position);
    }

    /// <summary>
    ///     What a click on a row does. A click selects (<see cref="OnSelected"/>) when the row isn't selected already, and a
    ///         second one on the same row inside the double-click time and range activates it (<see cref="OnActivated"/>).
    ///         Public so that a test can click without a mouse.
    /// </summary>
    /// <param name="index">The row.</param>
    /// <param name="position">Where the pointer was, for the double-click range.</param>
    public void PressRow(int index, Vector2 position)
    {
        if (index < 0 || index >= _rows.Count)
            return;

        GrabKeyboardFocus();

        // The engine's own line edit decides a double click the same way.
        var timing = IoCManager.Resolve<IGameTiming>();
        var configuration = IoCManager.Resolve<IConfigurationManager>();
        var now = timing.RealTime;
        var doubleClick = _lastPressedIndex == index
            && now - _lastPressedTime <= TimeSpan.FromMilliseconds(configuration.GetCVar(CVars.DoubleClickDelay))
            && (position - _lastPressedPosition).Length() <= configuration.GetCVar(CVars.DoubleClickRange);

        _lastPressedIndex = doubleClick ? -1 : index;
        _lastPressedTime = now;
        _lastPressedPosition = position;

        // tgui: onClick selects when it isn't already, and the double click that follows activates.
        if (_selectedIndex != index)
        {
            Select(index);
            OnSelected?.Invoke(index);
        }

        if (doubleClick)
            OnActivated?.Invoke(index);
    }

    /// <summary>
    ///     Moves the selection as the player's arrow keys do, scrolling it into view. Raises <see cref="OnSelected"/>.
    /// </summary>
    public void MoveSelection(int step)
    {
        if (_rows.Count == 0)
            return;

        var next = _selectedIndex is { } current ? Math.Clamp(current + step, 0, _rows.Count - 1) : step > 0 ? 0 : _rows.Count - 1;
        if (next == _selectedIndex)
            return;

        Select(next);
        ScrollIntoView(_rows[next]);
        OnSelected?.Invoke(next);
    }

    private void ScrollIntoView(Control row)
    {
        for (var ancestor = Parent; ancestor != null; ancestor = ancestor.Parent)
        {
            if (ancestor is not ScrollContainer scroll)
                continue;

            var scrolled = scroll.GetScrollValue();
            var top = row.GlobalPosition.Y - scroll.GlobalPosition.Y;
            var bottom = top + row.Height;

            if (top < 0f)
                scroll.SetScrollValue(new Vector2(scrolled.X, scrolled.Y + top));
            else if (bottom > scroll.Height)
                scroll.SetScrollValue(new Vector2(scrolled.X, scrolled.Y + bottom - scroll.Height));

            return;
        }
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);

        if (args.Function == EngineKeyFunctions.TextCursorUp)
        {
            MoveSelection(-1);
            args.Handle();
        }
        else if (args.Function == EngineKeyFunctions.TextCursorDown)
        {
            MoveSelection(1);
            args.Handle();
        }
        else if (args.Function == EngineKeyFunctions.TextSubmit && _selectedIndex is { } selected)
        {
            OnActivated?.Invoke(selected);
            args.Handle();
        }
    }
}
