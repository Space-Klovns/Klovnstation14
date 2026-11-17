using Content.Client._KS14.Tgui.Controls;
using Content.Shared.Research.Prototypes;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client._KS14.Tgui.Windows.Lathe;

/// <summary>
///     One queued batch in the tgui fabricator: its icon, its text and the move up, move down and remove buttons.
/// </summary>
public sealed class KsTguiLatheQueueRow : BoxContainer
{
    private ProtoId<LatheRecipePrototype>? _recipe;

    private readonly BoxContainer _displayContainer;
    private readonly Label _nameLabel;

    private int _index;

    public KsTguiLatheQueueRow(string displayText, int index, Control displayControl)
    {
        Orientation = LayoutOrientation.Horizontal;
        HorizontalExpand = true;
        SeparationOverride = 2;

        _displayContainer = new BoxContainer { MinSize = new System.Numerics.Vector2(32f, 32f), Margin = new Thickness(0f, 0f, 4f, 0f), VerticalAlignment = VAlignment.Center };
        _nameLabel = new Label { HorizontalExpand = true, VerticalAlignment = VAlignment.Center, ClipText = true };
        AddChild(_displayContainer);
        AddChild(_nameLabel);

        AddChild(MakeButton(KsTguiIcons.ArrowUp, KsTguiColor.Transparent, "lathe-menu-move-up-tooltip", () => OnMoveUpPressed?.Invoke(_index)));
        AddChild(MakeButton(KsTguiIcons.ArrowDown, KsTguiColor.Transparent, "lathe-menu-move-down-tooltip", () => OnMoveDownPressed?.Invoke(_index)));
        AddChild(MakeButton(KsTguiIcons.Times, KsTguiColor.Bad, "lathe-menu-delete-item-tooltip", () => OnDeletePressed?.Invoke(_index)));

        SetDisplayText(displayText);
        SetDisplayControl(displayControl);
        SetIndex(index);
    }

    public Action<int>? OnDeletePressed;
    public Action<int>? OnMoveUpPressed;
    public Action<int>? OnMoveDownPressed;

    private static KsTguiButton MakeButton(string icon, KsTguiColor color, string tooltipKey, Action pressed)
    {
        var button = new KsTguiButton { Icon = icon, Color = color, Compact = true, ToolTip = Loc.GetString(tooltipKey) };
        button.OnPressed += _ => pressed();
        return button;
    }

    public void SetDisplayText(string displayText)
    {
        _nameLabel.Text = displayText;
    }

    /// <summary>
    ///     Notes the recipe this row shows. False means it already showed it, and has the right icon.
    /// </summary>
    public bool TakeRecipe(ProtoId<LatheRecipePrototype> recipe)
    {
        if (_recipe == recipe)
            return false;

        _recipe = recipe;
        return true;
    }

    public void SetDisplayControl(Control displayControl)
    {
        _displayContainer.Children.Clear();
        _displayContainer.AddChild(displayControl);
    }

    public void SetIndex(int index)
    {
        _index = index;
    }
}
