using System.Numerics;
using Content.Client._KS14.Tgui.Controls;
using Content.Shared.Materials;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client._KS14.Tgui.Windows.Materials;

/// <summary>
///     One material in a <see cref="KsTguiMaterialList"/>: its icon, an easing amount and, when the storage allows it, buttons
///         that eject 1, 5 or 10 sheets. Its width comes from the layout it is given and never from the amount, as tgui's
///         <c>Flex.Item grow basis</c> in <c>MaterialAccessBar</c> does, so a number that grows wider cannot move its neighbours.
/// </summary>
public sealed class KsTguiMaterialEntry : Control
{
    /// <summary>
    ///     The narrowest a cell is, unless its buttons need more.
    /// </summary>
    public const float CellWidth = 96f;

    private const float Gap = 2f;

    private static readonly int[] EjectAmounts = [1, 5, 10];

    private readonly TextureRect _icon;
    private readonly BoxContainer _buttonRow;
    private readonly List<KsTguiButton> _buttons = new();

    private readonly string _unit;

    private float? _sheets;

    public KsTguiMaterialEntry(ProtoId<MaterialPrototype> material, Texture? icon, string unit, string materialName, Action<int> ejectPressed)
    {
        Material = material;
        MaterialName = materialName;
        _unit = unit;

        // The tooltip is this control's, and a control that ignores the mouse is never the one hovered.
        MouseFilter = MouseFilterMode.Stop;

        _icon = new TextureRect { Texture = icon, MinSize = new Vector2(32f, 32f) };

        AmountNumber = new KsTguiAnimatedNumber
        {
            Align = Label.AlignMode.Center,
            // A clipped label reports no width of its own, so no value can widen the cell. The cell is wide enough not to cut
            //      into a number: see ThreeFigures.
            ClipText = true,
            Format = amount => FormatAmount(ThreeFigures(amount)),
        };

        _buttonRow = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 1 };
        foreach (var amount in EjectAmounts)
        {
            var sheetsToEject = amount;
            var button = new KsTguiButton
            {
                Text = Loc.GetString("ks-tgui-lathe-recipe-times", ("amount", sheetsToEject)),
                Color = KsTguiColor.Transparent,
                Compact = true,
                ToolTip = Loc.GetString("lathe-menu-tooltip-display", ("amount", sheetsToEject), ("material", materialName)),
            };
            button.OnPressed += _ => ejectPressed(sheetsToEject);
            _buttons.Add(button);
            _buttonRow.AddChild(button);
        }

        AddChild(_icon);
        AddChild(AmountNumber);
        AddChild(_buttonRow);
    }

    public ProtoId<MaterialPrototype> Material { get; }

    public string MaterialName { get; }

    public KsTguiAnimatedNumber AmountNumber { get; }

    /// <summary>
    ///     Whether the eject buttons are there at all.
    /// </summary>
    public bool CanEject
    {
        get => _buttonRow.Visible;
        set
        {
            if (_buttonRow.Visible == value)
                return;

            _buttonRow.Visible = value;
            InvalidateMeasure();
        }
    }

    /// <summary>
    ///     Shows <paramref name="sheets"/> of the material and enables the eject buttons the stock covers.
    /// </summary>
    public void SetAmount(float sheets)
    {
        // The list calls this every frame, and the tooltip is a Fluent format.
        if (_sheets == sheets)
            return;

        _sheets = sheets;
        AmountNumber.Value = sheets;

        var ejectableSheets = (int)MathF.Floor(sheets);
        for (var i = 0; i < _buttons.Count; i++)
            _buttons[i].Disabled = ejectableSheets < EjectAmounts[i];

        ToolTip = Loc.GetString(
            "lathe-menu-material-display",
            ("material", MaterialName),
            ("amount", FormatAmount(sheets)));
    }

    /// <summary>
    ///     What tg's <c>LABEL_FORMAT</c> (<c>formatSiUnit(value, 0)</c>) does to the number: three significant figures, whatever
    ///         the value. That, more than the cell, is what keeps a moving number readable: <c>110.15</c> is wider than
    ///         <c>105</c>, and flickers its decimals past on the way there. The tooltip has the exact amount.
    /// </summary>
    public static float ThreeFigures(float sheets)
    {
        return sheets >= 100f
            ? MathF.Round(sheets)
            : MathF.Round(sheets, sheets >= 10f ? 1 : 2);
    }

    private string FormatAmount(float sheets)
    {
        return Loc.GetString("lathe-menu-material-amount", ("amount", sheets), ("unit", _unit));
    }

    private float MinimumWidth()
    {
        _buttonRow.Measure(new Vector2(float.PositiveInfinity, float.PositiveInfinity));
        return MathF.Max(CellWidth, _buttonRow.DesiredSize.X);
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var minimum = MinimumWidth();
        var width = float.IsInfinity(availableSize.X) ? minimum : MathF.Max(availableSize.X, minimum);

        _icon.Measure(new Vector2(width, float.PositiveInfinity));
        AmountNumber.Measure(new Vector2(width, float.PositiveInfinity));
        _buttonRow.Measure(new Vector2(width, float.PositiveInfinity));

        var height = _icon.DesiredSize.Y + Gap + AmountNumber.DesiredSize.Y;
        if (_buttonRow.Visible)
            height += Gap + _buttonRow.DesiredSize.Y;

        return new Vector2(width, height);
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        var y = 0f;

        var iconWidth = _icon.DesiredSize.X;
        _icon.Arrange(UIBox2.FromDimensions((finalSize.X - iconWidth) / 2f, y, iconWidth, _icon.DesiredSize.Y));
        y += _icon.DesiredSize.Y + Gap;

        AmountNumber.Arrange(UIBox2.FromDimensions(0f, y, finalSize.X, AmountNumber.DesiredSize.Y));
        y += AmountNumber.DesiredSize.Y + Gap;

        if (_buttonRow.Visible)
        {
            var rowWidth = MathF.Min(_buttonRow.DesiredSize.X, finalSize.X);
            _buttonRow.Arrange(UIBox2.FromDimensions((finalSize.X - rowWidth) / 2f, y, rowWidth, _buttonRow.DesiredSize.Y));
        }

        return finalSize;
    }
}
