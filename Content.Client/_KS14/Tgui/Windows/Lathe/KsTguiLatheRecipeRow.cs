using Content.Client._KS14.Tgui.Controls;
using Content.Client.Lathe;
using Content.Client.Lathe.UI;
using Content.Shared.Research.Prototypes;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using XColor = Robust.Shared.Maths.Color;

namespace Content.Client._KS14.Tgui.Windows.Lathe;

/// <summary>
///     One design in the tgui fabricator's list: a description button, the design itself (queues the amount the window is
///         set to) and the x5 and x10 buttons.
/// </summary>
public sealed class KsTguiLatheRecipeRow : BoxContainer
{
    private static readonly XColor Clear = XColor.Black.WithAlpha(0f);
    private static readonly XColor HoverWash = KsTguiPalettes.Translucent(XColor.FromHex("#606060"), 0.25f, KsTguiPalettes.Base);

    private readonly LatheSystem _latheSystem;
    private readonly KsTguiButton _helpButton;
    private readonly TitleButton _titleButton;
    private readonly KsTguiStyleBox _titleBox = new(Clear, radius: 2f);
    private readonly BoxContainer _displayContainer;
    private readonly Label _nameLabel;
    private readonly KsTguiButton _fiveButton;
    private readonly KsTguiButton _tenButton;

    private ProtoId<LatheRecipePrototype> _recipeId;
    private Func<string> _tooltipSupplier = () => string.Empty;
    private string _description = string.Empty;

    public KsTguiLatheRecipeRow(LatheSystem latheSystem)
    {
        _latheSystem = latheSystem;
        Orientation = LayoutOrientation.Horizontal;
        HorizontalExpand = true;
        SeparationOverride = 2;

        _helpButton = new KsTguiButton { Icon = KsTguiIcons.Question, Color = KsTguiColor.Transparent, Compact = true };
        _helpButton.TooltipSupplier = _ => string.IsNullOrWhiteSpace(_description) ? null : new RecipeTooltip(_description);
        AddChild(_helpButton);

        _titleButton = new TitleButton { HorizontalExpand = true, StyleBoxOverride = _titleBox };
        _titleBox.SetContentMarginOverride(StyleBox.Margin.All, 2f);
        _displayContainer = new BoxContainer { MinSize = new System.Numerics.Vector2(32f, 32f), Margin = new Thickness(0f, 0f, 4f, 0f), VerticalAlignment = VAlignment.Center };
        _nameLabel = new Label { HorizontalExpand = true, VerticalAlignment = VAlignment.Center, ClipText = true };
        var titleRow = new BoxContainer { Orientation = LayoutOrientation.Horizontal, MouseFilter = MouseFilterMode.Ignore };
        titleRow.AddChild(_displayContainer);
        titleRow.AddChild(_nameLabel);
        _titleButton.AddChild(titleRow);
        _titleButton.TooltipSupplier = _ => new RecipeTooltip(_tooltipSupplier());
        _titleButton.OnPressed += _ => OnBuild?.Invoke(_recipeId, 0);
        _titleButton.DrawModeRefreshed += () => RefreshLook(_titleButton);
        AddChild(_titleButton);

        _fiveButton = MakeAmountButton(5);
        _tenButton = MakeAmountButton(10);
    }

    /// <summary>
    ///     The recipe and an amount; 0 asks for the window's own amount.
    /// </summary>
    public event Action<string, int>? OnBuild;

    private KsTguiButton MakeAmountButton(int amount)
    {
        var button = new KsTguiButton { Text = Loc.GetString("ks-tgui-lathe-recipe-times", ("amount", amount)), Color = KsTguiColor.Transparent, Compact = true };
        button.OnPressed += _ => OnBuild?.Invoke(_recipeId, amount);
        AddChild(button);
        return button;
    }

    public void SetRecipe(LatheRecipePrototype recipe, Func<string> tooltipSupplier, bool canProduce, bool canProduceFive, bool canProduceTen, Control displayControl)
    {
        _recipeId = recipe.ID;
        _nameLabel.Text = _latheSystem.GetRecipeName(recipe);
        _description = _latheSystem.GetRecipeDescription(recipe);
        _helpButton.Visible = !string.IsNullOrWhiteSpace(_description);
        _tooltipSupplier = tooltipSupplier;

        _titleButton.Disabled = !canProduce;
        _fiveButton.Disabled = !canProduceFive;
        _tenButton.Disabled = !canProduceTen;

        _displayContainer.Children.Clear();
        _displayContainer.AddChild(displayControl);
        RefreshLook(_titleButton);
    }

    private void RefreshLook(ContainerButton button)
    {
        _titleBox.BackgroundColor = button.DrawMode == BaseButton.DrawModeEnum.Hover && !button.Disabled ? HoverWash : Clear;
        _nameLabel.FontColorOverride = button.Disabled ? KsTguiPalettes.TextDim : null;
    }

    private sealed class TitleButton : ContainerButton
    {
        public event Action? DrawModeRefreshed;

        protected override void DrawModeChanged()
        {
            base.DrawModeChanged();
            DrawModeRefreshed?.Invoke();
        }
    }
}
