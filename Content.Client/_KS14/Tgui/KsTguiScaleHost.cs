using System.Numerics;
using Content.Client._KS14.Tgui.Backdrop;
using Robust.Client.UserInterface;

namespace Content.Client._KS14.Tgui;

/// <summary>
///     Scales what it holds about the middle of its top edge, as tgui's Floating does to a menu opening below its button. A
///         control can't scale itself, so this stands between a popup and its contents.
/// </summary>
/// <remarks>
///     With <see cref="Backdrop"/> set it also blurs what is behind the menu, as tgui's <c>backdrop-filter</c> does, drawn
///         under the contents at the scale and fade the host has. A menu that is closing is drawn by
///         <see cref="KsTguiDropdownGhost"/> without being in the tree, which then says in <see cref="BackdropAnchor"/> where
///         in the tree to take the backdrop from.
/// </remarks>
public sealed class KsTguiScaleHost : Control
{
    /// <summary>
    ///     <c>--dropdown-menu-border-radius</c> is <c>--border-radius-large</c>.
    /// </summary>
    public const float MenuRadius = 6f;

    public float Scale = 1f;

    /// <summary>
    ///     Where the blur comes from; null for none.
    /// </summary>
    public KsTguiBackdropUIController? Backdrop { get; set; }

    /// <summary>
    ///     A control in the tree standing in for this host's place there, while this one is drawn from outside it. Its position
    ///         is <see cref="BackdropAnchorPosition"/>.
    /// </summary>
    public Control? BackdropAnchor { get; set; }

    public Vector2i BackdropAnchorPosition { get; set; }

    /// <summary>
    ///     Where on screen this host is drawn while <see cref="BackdropAnchor"/> is set, as a ghost draws it out of the tree.
    /// </summary>
    public Vector2i BackdropHostPosition { get; set; }

    private KsTguiBackdropBlur? _blur;
    private float _lastUiScale = 1f;

    /// <summary>
    ///     Lets go of the blur's render targets. They are made again if the host is drawn with a blur.
    /// </summary>
    public void ReleaseBackdrop()
    {
        _blur?.Dispose();
        _blur = null;
    }

    protected override void PreRenderChildren(ref ControlRenderArguments args)
    {
        base.PreRenderChildren(ref args);

        // A host in a popup that has been taken out of the tree has no scale of its own to ask.
        if (IsInsideTree)
            _lastUiScale = UIScale;

        // The handle scales what each child draws, and the coordinate transform moves where each one sits, about the pivot.
        var pivotShift = (1f - Scale) * PixelWidth / 2f;

        if (Backdrop != null)
        {
            _blur ??= new KsTguiBackdropBlur(Backdrop);

            // Not args.Position: the engine leaves that unset for PreRenderChildren, so it is always the screen's corner.
            //      Out of the tree, drawn by a ghost, the ghost says where.
            var position = BackdropAnchor != null ? BackdropHostPosition : GlobalPixelPosition;
            var box = UIBox2.FromDimensions(position + new Vector2(pivotShift, 0f), (Vector2)PixelSize * Scale);
            var anchor = BackdropAnchor ?? this;
            var anchorPosition = BackdropAnchor != null ? BackdropAnchorPosition : GlobalPixelPosition;
            _blur.Draw(args.Handle, anchor, anchorPosition, box, PixelSize, MenuRadius * _lastUiScale * Scale, args.Modulate);
        }

        if (Scale >= 1f)
            return;

        args.CoordinateTransform = Matrix3x2.CreateScale(Scale) * Matrix3x2.CreateTranslation(pivotShift, 0f);
        args.Handle.DrawingHandleScreen.SetTransform(Matrix3x2.CreateScale(Scale));
    }
}
