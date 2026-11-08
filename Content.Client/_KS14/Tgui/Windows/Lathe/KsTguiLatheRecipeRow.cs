using Content.Client._KS14.Tgui.Controls;
using Content.Client._KS14.Tgui.Tooltips;
using Content.Client._KS14.Tgui.Windows.Materials;
using Content.Client.Lathe;
using Content.Shared.Research.Prototypes;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using XColor = Robust.Shared.Maths.Color;

namespace Content.Client._KS14.Tgui.Windows.Lathe;

/// <summary>
///     One design in the tgui fabricator's list (tg's <c>AutolatheRecipe</c> and <c>.FabricatorRecipe</c>): a description
///         button, the design itself (queues one, as tg's <c>multiplier: 1</c>) and the x5 and x10 buttons, with a line under
///         each row but the last. Hovering the design or an amount shows what it costs.
/// </summary>
public sealed class KsTguiLatheRecipeRow : BoxContainer
{
    /// <summary>
    ///     FA's <c>circle-question</c>, tg's <c>question-circle</c>.
    /// </summary>
    private const string QuestionCircle = "";

    /// <summary>
    ///     <c>border-bottom: var(--border-thickness-tiny) solid hsla(0, 0%, 0%, 0.5)</c>.
    /// </summary>
    private static readonly XColor Separator = KsTguiPalettes.Translucent(XColor.Black, 0.5f, KsTguiPalettes.Base);

    private static readonly XColor Clear = XColor.Black.WithAlpha(0f);
    private static readonly XColor HoverWash = KsTguiPalettes.Translucent(XColor.FromHex("#606060"), 0.25f, KsTguiPalettes.Base);

    /// <summary>
    ///     <c>.FabricatorRecipe__Title--disabled { opacity: 0.5 }</c>, as the alpha that gives it here.
    /// </summary>
    private static readonly float DisabledOpacity = KsTguiPalettes.Translucent(XColor.White, 0.5f, KsTguiPalettes.Base).A;

    private readonly LatheSystem _latheSystem;
    private readonly KsTguiButton _helpButton;
    private readonly TitleButton _titleButton;
    private readonly KsTguiStyleBox _titleBox = new(Clear);
    private readonly BoxContainer _displayContainer;
    private readonly Label _nameLabel;
    private readonly KsTguiButton _fiveButton;
    private readonly KsTguiButton _tenButton;
    private readonly KsTguiInputButton _maxButton;

    private ProtoId<LatheRecipePrototype> _recipeId;
    private Func<int, IReadOnlyList<KsTguiMaterialCost>> _costs = _ => [];
    private string _description = string.Empty;
    private bool _canProduce;
    private bool _canProduceFive;
    private bool _canProduceTen;
    private int _maxAmount;

    public KsTguiLatheRecipeRow(LatheSystem latheSystem)
    {
        _latheSystem = latheSystem;
        Orientation = LayoutOrientation.Horizontal;
        HorizontalExpand = true;

        // tg's padding: 0 var(--space-xl) on the icon button.
        _helpButton = new KsTguiButton { Icon = QuestionCircle, Color = KsTguiColor.Transparent, Compact = true, Margin = new Thickness(0f, 0f, 0f, 1f) };
        _helpButton.TooltipSupplier = _ => string.IsNullOrWhiteSpace(_description) ? null : KsTguiTooltip.Create(_description);
        KsTguiTooltip.SetPlacement(_helpButton, KsTguiTooltipPlacement.Right);
        AddChild(_helpButton);

        // tg's padding: var(--space-s) var(--space-m), and the icon 32px with var(--space-xl) after it.
        _titleButton = new TitleButton { HorizontalExpand = true, StyleBoxOverride = _titleBox, ModulateSelfOverride = XColor.White };
        _titleBox.SetContentMarginOverride(StyleBox.Margin.Vertical, 3f);
        _titleBox.SetContentMarginOverride(StyleBox.Margin.Horizontal, 6f);
        _displayContainer = new BoxContainer { MinSize = new System.Numerics.Vector2(32f, 32f), Margin = new Thickness(0f, 0f, 12f, 0f), VerticalAlignment = VAlignment.Center };
        _nameLabel = new Label { HorizontalExpand = true, VerticalAlignment = VAlignment.Center, ClipText = true };
        var titleRow = new BoxContainer { Orientation = LayoutOrientation.Horizontal, MouseFilter = MouseFilterMode.Ignore };
        titleRow.AddChild(_displayContainer);
        titleRow.AddChild(_nameLabel);
        _titleButton.AddChild(titleRow);
        _titleButton.TooltipSupplier = _ => CostTooltip(1);
        _titleButton.OnPressed += _ =>
        {
            if (_canProduce)
                OnBuild?.Invoke(_recipeId, 1); // tg: multiplier: 1
        };
        _titleButton.DrawModeRefreshed += () => RefreshLook(_titleButton);
        AddChild(_titleButton);

        _fiveButton = MakeAmountButton(5);
        _tenButton = MakeAmountButton(10);

        // tg's Button.Input, transparent, labelled [Max: N]: a typed number queues that many.
        _maxButton = new KsTguiInputButton
        {
            Color = KsTguiColor.Transparent,
            Compact = true,
            VerticalAlignment = VAlignment.Stretch,
        };
        _maxButton.Committed += OnMaxCommitted;

        // Not tg: its row is a plain flex row, so the x5 and x10 shift with the digits of N. The button sits over an invisible
        //      copy labelled with the widest N, in the same font and padding, so the host is always as wide as the widest label
        //      and the text stays left aligned. Measured from the real button, so it follows the language and the UI scale.
        var widest = new KsTguiButton
        {
            Color = KsTguiColor.Transparent,
            Compact = true,
            VerticalAlignment = VAlignment.Stretch,
            Text = Loc.GetString("ks-tgui-lathe-recipe-max", ("amount", MaxPrintsCap)),
            Modulate = XColor.White.WithAlpha(0f),
            MouseFilter = MouseFilterMode.Ignore,
        };
        MaxHost = new Control();
        MaxHost.AddChild(widest);
        MaxHost.AddChild(_maxButton);
        AddChild(MaxHost);
    }

    /// <summary>
    ///     tg's most prints at once: <c>Math.min(Math.floor(maxmult), 50)</c>.
    /// </summary>
    public const int MaxPrintsCap = 50;

    /// <summary>
    ///     How many prints the machine can make now, at most <see cref="MaxPrintsCap"/>.
    /// </summary>
    public int MaxAmount => _maxAmount;

    public ProtoId<LatheRecipePrototype> RecipeId => _recipeId;

    /// <summary>
    ///     The box the [Max: N] button fills, as wide as the widest label it can have.
    /// </summary>
    public Control MaxHost { get; }

    public KsTguiButton FiveButton => _fiveButton;

    public KsTguiButton TenButton => _tenButton;

    public KsTguiButton MaxButton => _maxButton;

    /// <summary>
    ///     tg's <c>onCommit</c>, behind <c>canPrint</c> as the other buttons are: queues what was typed.
    /// </summary>
    private void OnMaxCommitted(string text)
    {
        // Back to the label, so typing the same number again is a commit too.
        _maxButton.Value = string.Empty;

        if (_maxAmount > 0 && int.TryParse(text.Trim(), out var amount) && amount > 0)
            OnBuild?.Invoke(_recipeId, amount);
    }

    /// <summary>
    ///     The recipe and how many to make.
    /// </summary>
    public event Action<string, int>? OnBuild;

    private KsTguiButton MakeAmountButton(int amount)
    {
        var button = new KsTguiButton
        {
            Text = Loc.GetString("ks-tgui-lathe-recipe-times", ("amount", amount)),
            Color = KsTguiColor.Transparent,
            Compact = true,
            VerticalAlignment = VAlignment.Stretch,
        };
        // tg's onClick is `canPrint && act(...)`: a design that can't be made still hovers, and a click does nothing.
        button.OnPressed += _ =>
        {
            if (amount == 5 ? _canProduceFive : _canProduceTen)
                OnBuild?.Invoke(_recipeId, amount);
        };
        button.TooltipSupplier = _ => CostTooltip(amount);
        AddChild(button);
        return button;
    }

    private Control? CostTooltip(int amount)
    {
        var costs = _costs(amount);
        return costs.Count == 0 ? null : KsTguiTooltip.Create(new KsTguiMaterialCostSequence(costs));
    }

    /// <param name="costs">What the design takes, for a given number of prints, with what the machine has.</param>
    /// <param name="displayControl">
    ///     Makes the icon, only when the row's recipe changes. This runs on every state, so on every change of materials, and a
    ///         new icon is a new entity.
    /// </param>
    public void SetRecipe(LatheRecipePrototype recipe, Func<int, IReadOnlyList<KsTguiMaterialCost>> costs, bool canProduce, bool canProduceFive,
        bool canProduceTen, int maxAmount, Func<Control> displayControl)
    {
        var sameRecipe = _recipeId == recipe.ID && _displayContainer.ChildCount > 0;
        _recipeId = recipe.ID;
        _nameLabel.Text = _latheSystem.GetRecipeName(recipe);
        _description = _latheSystem.GetRecipeDescription(recipe);
        _helpButton.Visible = !string.IsNullOrWhiteSpace(_description);
        _costs = costs;

        // tg greys the description button out with the design, though it still works.
        _canProduce = canProduce;
        _canProduceFive = canProduceFive;
        _canProduceTen = canProduceTen;
        _helpButton.Modulate = Opacity(canProduce);
        _titleButton.Modulate = Opacity(canProduce);
        SetAmountButton(_fiveButton, canProduceFive);
        SetAmountButton(_tenButton, canProduceTen);
        _maxAmount = maxAmount;
        _maxButton.ButtonText = Loc.GetString("ks-tgui-lathe-recipe-max", ("amount", maxAmount));
        _maxButton.Modulate = Opacity(maxAmount > 0);
        _maxButton.Strikethrough = maxAmount <= 0;

        if (!sameRecipe)
        {
            _displayContainer.Children.Clear();
            _displayContainer.AddChild(displayControl());
        }

        RefreshLook(_titleButton);
    }

    /// <summary>
    ///     <c>.FabricatorRecipe__Button--disabled</c>: half opacity and struck through. It isn't a disabled tgui Button, which
    ///         would be red; the tooltip still shows what's missing.
    /// </summary>
    private static void SetAmountButton(KsTguiButton button, bool canProduce)
    {
        button.Modulate = Opacity(canProduce);
        button.Strikethrough = !canProduce;
    }

    /// <summary>
    ///     tg's <c>opacity: 0.5</c> on whatever can't be made, icon and all.
    /// </summary>
    private static XColor Opacity(bool canProduce)
    {
        return canProduce ? XColor.White : XColor.White.WithAlpha(DisabledOpacity);
    }

    /// <summary>
    ///     Exposed for tests: whether the row would queue anything for that many.
    /// </summary>
    public bool CanBuild(int amount)
    {
        return amount switch
        {
            5 => _canProduceFive,
            10 => _canProduceTen,
            _ => _canProduce,
        };
    }

    private void RefreshLook(ContainerButton button)
    {
        _titleBox.BackgroundColor = button.DrawMode == BaseButton.DrawModeEnum.Hover ? HoverWash : Clear;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        if (Parent is not { } parent || GetPositionInParent() == parent.ChildCount - 1)
            return;

        var thickness = MathF.Max(1f, MathF.Round(UIScale));
        handle.DrawRect(new UIBox2(0f, PixelSize.Y - thickness, PixelSize.X, PixelSize.Y), Separator);
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
