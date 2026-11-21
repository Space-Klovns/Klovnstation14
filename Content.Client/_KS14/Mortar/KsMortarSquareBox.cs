using System.Numerics;
using Robust.Client.UserInterface;

namespace Content.Client._KS14.Mortar;

/// <summary>
///     Lays its children over each other in the largest square that fits, centred: the mortar's sight stays square, and its
///         black backing with it, however its section is stretched.
/// </summary>
public sealed class KsMortarSquareBox : Control
{
    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var side = 0f;
        foreach (var child in Children)
        {
            child.Measure(availableSize);
            side = MathF.Max(side, MathF.Max(child.DesiredSize.X, child.DesiredSize.Y));
        }

        return new Vector2(side, side);
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        var side = MathF.Min(finalSize.X, finalSize.Y);
        var box = UIBox2.FromDimensions((finalSize - new Vector2(side, side)) / 2f, new Vector2(side, side));
        foreach (var child in Children)
        {
            child.Arrange(box);
        }

        return finalSize;
    }
}
