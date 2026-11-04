using System.Collections;
using Robust.Client.UserInterface;

namespace Content.Client._KS14.UserInterface.Tgui;

/// <summary>
///     A <see cref="Control.XamlChildren"/> collection that sends each child written in xaml to wherever its owner
///         wants it, such as a section's title row or its body. Reading it reads the body's children.
/// </summary>
public sealed class KsTguiChildRouter : ICollection<Control>, IReadOnlyCollection<Control>
{
    private readonly Control _body;
    private readonly Action<Control> _add;

    public KsTguiChildRouter(Control body, Action<Control> add)
    {
        _body = body;
        _add = add;
    }

    public int Count => _body.ChildCount;

    public bool IsReadOnly => false;

    public void Add(Control item)
    {
        _add(item);
    }

    public void Clear()
    {
        _body.RemoveAllChildren();
    }

    public bool Contains(Control item)
    {
        return item.Parent == _body;
    }

    public void CopyTo(Control[] array, int arrayIndex)
    {
        _body.Children.CopyTo(array, arrayIndex);
    }

    public bool Remove(Control item)
    {
        if (item.Parent != _body)
            return false;

        _body.RemoveChild(item);
        return true;
    }

    public IEnumerator<Control> GetEnumerator()
    {
        return _body.Children.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
