using Content.Client.UserInterface.Systems.Chat.Controls;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.Tgui;

/// <summary>
///     Matches a plain button stacked in a vertical <see cref="BoxContainer"/> that leaves its children no gap of its own, so the
///         tgui sheet can give it tg's 2px <c>margin-bottom</c> there and nowhere else. tg puts that margin under every button; here
///         it would leave a button in a row 2px shorter than a LineEdit beside it, as a box stretches each child into its slot minus
///         its margin. No element selector can see a box's orientation, hence a selector of its own.
/// </summary>
/// <remarks>
///     <list type="bullet">
///         <item>It runs when the button is styled, which the engine does whenever the button is parented. So a box filled from
///             code after its window opened is covered, and nothing runs per frame. Changing a box's orientation or separation later
///             leaves its buttons as they were styled.</item>
///         <item>A box that sets a separation already spaces its children as its author meant, and one with
///             <see cref="KsTguiSheetlet.StyleClassButtonStack"/> has the gap as separation instead, for buttons whose margin is
///             written in XAML or code: either would win over the sheet.</item>
///         <item>Every button in the box gets it, the last one too: a style is worked out when the button is parented, before
///             any sibling after it exists, so "last child" can't be known then.</item>
///     </list>
/// </remarks>
public sealed class KsTguiButtonStackSelector : Selector
{
    public override bool Matches(Control control)
    {
        if (control is not (Button or OptionButton) || !control.HasStyleClass(ContainerButton.StyleClassButton))
            return false;

        if (control.HasStyleClass(ChatInputBox.StyleClassChatFilterOptionButton))
            return false;

        return control.Parent is BoxContainer { Orientation: BoxContainer.LayoutOrientation.Vertical, SeparationOverride: null or 0 } box
            && !box.HasStyleClass(KsTguiSheetlet.StyleClassButtonStack);
    }

    /// <summary>
    ///     Above the sheet's own button margin rules (one class and the button's type), as an element selector with a second
    ///         class would be.
    /// </summary>
    public override StyleSpecificity CalculateSpecificity()
    {
        return new StyleSpecificity(0, 2, 0);
    }
}
