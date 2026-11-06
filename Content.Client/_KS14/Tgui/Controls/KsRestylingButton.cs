using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.Tgui.Controls;

/// <summary>
///     A <see cref="Button"/> that restyles all its children when its own style changes. <see cref="Button"/> only does so
///         for its built-in label, so an extra child, such as an icon label, keeps its enabled colour on a disabled button.
///         Under Baron it behaves as a Button.
/// </summary>
public sealed class KsRestylingButton : Button
{
    protected override void StylePropertiesChanged()
    {
        base.StylePropertiesChanged();

        if (!TryGetStyleProperty<bool>(KsTguiSheetlet.StylePropertyRestyleChildren, out var restyle) || !restyle)
            return;

        foreach (var child in Children)
        {
            if (child != Label)
                child.Restyle();
        }
    }
}
