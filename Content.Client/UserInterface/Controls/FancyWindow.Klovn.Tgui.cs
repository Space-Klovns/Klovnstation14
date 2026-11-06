using System.Numerics;
using Content.Client._KS14.Tgui;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;

namespace Content.Client.UserInterface.Controls;

// The parts of the window KsTguiChromeUIController needs.
public partial class FancyWindow
{
    private string? _ksRawTitle;
    private bool _ksChromed;

    /// <summary>
    ///     Whether the tgui chrome is on this window. Changes how it shows its title and where it can be dragged.
    /// </summary>
    public bool KsChromed
    {
        get => _ksChromed;
        set
        {
            _ksChromed = value;
            KsRefreshTitle();
        }
    }

    public PanelContainer KsHeader => WindowHeader;

    public Label KsTitleLabel => WindowTitle;

    public TextureButton KsCloseButton => CloseButton;

    private void KsRefreshTitle()
    {
        WindowTitle.Text = _ksChromed && _ksRawTitle != null ? KsTguiText.FormatTitle(_ksRawTitle) : _ksRawTitle;
    }

    /// <summary>
    ///     Covers the window's edges so contents that stop clicks right up to the border don't take presses meant for the
    ///         resize zones. Asks <see cref="GetDragModeFor"/>, so a window that overrides it keeps its own zones.
    /// </summary>
    private void KsAddEdgeGrip()
    {
        AddChild(new KsWindowEdgeGrip(
            point => (GetDragModeFor(point) & ~DragMode.Move) != DragMode.None && (_ksChromed || KsInEdgeStrip(point)),
            point =>
            {
                var mode = GetDragModeFor(point) & ~DragMode.Move;
                return mode switch
                {
                    DragMode.Top or DragMode.Bottom => CursorShape.VResize,
                    DragMode.Left or DragMode.Right => CursorShape.HResize,
                    DragMode.Bottom | DragMode.Left or DragMode.Top | DragMode.Right => CursorShape.NESWResize,
                    DragMode.Bottom | DragMode.Right or DragMode.Top | DragMode.Left => CursorShape.NWSEResize,
                    _ => CursorShape.Arrow,
                };
            }));
    }

    /// <summary>
    ///     The stock zones are 7 wide, which would take most of a scrollbar lying against the edge. They're still there behind
    ///         the grip, wherever the contents leave a gap.
    /// </summary>
    private bool KsInEdgeStrip(Vector2 point)
    {
        const float stripWidth = 3f;

        return point.X <= stripWidth || point.Y <= stripWidth
            || point.X >= Size.X - stripWidth || point.Y >= Size.Y - stripWidth;
    }

    private DragMode KsTguiDragMode(Vector2 relativeMousePos)
    {
        return KsTguiWindowZones.Classify(relativeMousePos, Size, Resizable) switch
        {
            KsTguiDragZone.Move => DragMode.Move,
            KsTguiDragZone.Bottom => DragMode.Bottom,
            KsTguiDragZone.Right => DragMode.Right,
            KsTguiDragZone.BottomRight => DragMode.Bottom | DragMode.Right,
            _ => DragMode.None,
        };
    }
}
