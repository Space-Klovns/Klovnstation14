using System.Numerics;
using Robust.Client.UserInterface;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     tgui's Icon.Stack: icons layered on one another, each centred in the largest of them. <see cref="IconSize"/> scales
///         every <see cref="KsTguiIcon"/> in it, as tgui's font-size on the stack does.
/// </summary>
public sealed class KsTguiIconStack : Control
{
    private float _size = 1f;

    /// <summary>
    ///     Size in ems, as <see cref="KsTguiIcon.IconSize"/>, for the whole stack.
    /// </summary>
    public float IconSize
    {
        get => _size;
        set
        {
            _size = value;
            foreach (var child in Children)
            {
                if (child is KsTguiIcon icon)
                    icon.IconSize = value;
            }
        }
    }

    protected override void ChildAdded(Control newChild)
    {
        base.ChildAdded(newChild);

        if (newChild is KsTguiIcon icon)
            icon.IconSize = _size;
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var size = Vector2.Zero;
        foreach (var child in Children)
        {
            child.Measure(availableSize);
            size = Vector2.Max(size, child.DesiredSize);
        }

        return size;
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        foreach (var child in Children)
        {
            var offset = (finalSize - child.DesiredSize) / 2f;
            child.Arrange(UIBox2.FromDimensions(offset, child.DesiredSize));
        }

        return finalSize;
    }
}
