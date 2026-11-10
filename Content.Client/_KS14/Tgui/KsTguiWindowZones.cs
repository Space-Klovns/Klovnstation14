using System.Numerics;

namespace Content.Client._KS14.Tgui;

/// <summary>
///     What a press on a window does, by where it lands.
/// </summary>
public enum KsTguiDragZone : byte
{
    None,
    Move,
    Bottom,
    Right,
    BottomRight,
}

/// <summary>
///     tgui's window handles. There are no north or west ones, and the rest are from <c>Window.tsx</c>.
/// </summary>
public static class KsTguiWindowZones
{
    private const float BottomHandle = 6f;
    private const float RightHandle = 3f;
    private const float CornerHandle = 20f;

    public static KsTguiDragZone Classify(Vector2 position, Vector2 size, bool resizable)
    {
        if (resizable)
        {
            if (position.X > size.X - CornerHandle && position.Y > size.Y - CornerHandle)
                return KsTguiDragZone.BottomRight;

            if (position.Y > size.Y - BottomHandle)
                return KsTguiDragZone.Bottom;

            if (position.X > size.X - RightHandle)
                return KsTguiDragZone.Right;
        }

        return position.Y < KsTguiGeometry.TitleBarHeight ? KsTguiDragZone.Move : KsTguiDragZone.None;
    }
}
