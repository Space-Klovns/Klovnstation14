using Robust.Shared.Maths;

namespace Content.Shared._KS14.Procedural;

/// <summary>
/// Conservative tile-center supercover ray. An exact corner crossing inspects both side cells.
/// Callers must cap endpoint range and work count before invoking this loop.
/// </summary>
internal static class KsProcgenTileRay
{
    public static bool IsClear(Vector2i first, Vector2i second,
        IReadOnlySet<Vector2i> blockingCells)
    {
        var deltaX = (long) second.X - first.X;
        var deltaY = (long) second.Y - first.Y;
        var lengthX = Math.Abs(deltaX);
        var lengthY = Math.Abs(deltaY);
        var stepX = Math.Sign(deltaX);
        var stepY = Math.Sign(deltaY);
        var x = first.X;
        var y = first.Y;
        var advancedX = 0L;
        var advancedY = 0L;
        while (advancedX < lengthX || advancedY < lengthY)
        {
            var compare = (1 + 2 * advancedX) * lengthY -
                          (1 + 2 * advancedY) * lengthX;
            if (compare == 0)
            {
                if (blockingCells.Contains(new Vector2i(x + stepX, y)) ||
                    blockingCells.Contains(new Vector2i(x, y + stepY)))
                    return false;
                x += stepX;
                y += stepY;
                advancedX++;
                advancedY++;
            }
            else if (compare < 0)
            {
                x += stepX;
                advancedX++;
            }
            else
            {
                y += stepY;
                advancedY++;
            }

            if (blockingCells.Contains(new Vector2i(x, y)))
                return false;
        }

        return true;
    }
}
