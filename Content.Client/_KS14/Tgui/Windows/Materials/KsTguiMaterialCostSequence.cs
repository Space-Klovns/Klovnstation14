using System.Globalization;
using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.Tgui.Windows.Materials;

/// <summary>
///     One material of a <see cref="KsTguiMaterialCostSequence"/>.
/// </summary>
/// <param name="Needed">Material units the print takes.</param>
/// <param name="Available">Material units the machine has, or null where it doesn't matter.</param>
public readonly record struct KsTguiMaterialCost(Texture? Icon, int Needed, int? Available, int SheetVolume);

/// <summary>
///     tg's <c>MaterialCostSequence</c>, a fabricator's tooltip: each material's sprite in a row, with the sheets it takes under
///         it, coloured by whether there's enough.
/// </summary>
public sealed class KsTguiMaterialCostSequence : BoxContainer
{
    /// <summary>
    ///     tg hardcodes these in the component rather than using the palette.
    /// </summary>
    public static readonly Color Plenty = Color.FromHex("#ffffff");
    public static readonly Color OnlyOnce = Color.FromHex("#f08f11");
    public static readonly Color NotEnough = Color.FromHex("#db2828");

    /// <summary>
    ///     <c>style={{ padding: '0.25em' }}</c> around each material.
    /// </summary>
    private const float ItemPadding = 3f;

    public KsTguiMaterialCostSequence(IEnumerable<KsTguiMaterialCost> costs)
    {
        Orientation = LayoutOrientation.Horizontal;

        foreach (var cost in costs)
        {
            var item = new BoxContainer
            {
                Orientation = LayoutOrientation.Vertical,
                Margin = new Thickness(ItemPadding),
            };

            item.AddChild(new TextureRect
            {
                Texture = cost.Icon,
                MinSize = new Vector2(32f, 32f),
                SetSize = new Vector2(32f, 32f),
                Stretch = TextureRect.StretchMode.KeepCentered,
                HorizontalAlignment = HAlignment.Center,
            });

            item.AddChild(new Label
            {
                Text = FormatSheets((float)cost.Needed / cost.SheetVolume),
                FontColorOverride = cost.Available is { } available ? AmountColor(cost.Needed, available) : null,
                HorizontalAlignment = HAlignment.Center,
            });

            AddChild(item);
        }
    }

    /// <summary>
    ///     White with enough for twice over, orange for only once, red for not at all.
    /// </summary>
    public static Color AmountColor(int needed, int available)
    {
        if (needed * 2 <= available)
            return Plenty;

        return needed <= available ? OnlyOnce : NotEnough;
    }

    /// <summary>
    ///     tg's <c>formatSiUnit(sheets, 0)</c>: three significant figures, with no unit for sheets under a thousand.
    /// </summary>
    public static string FormatSheets(float sheets)
    {
        return KsTguiMaterialEntry.ThreeFigures(sheets).ToString(CultureInfo.InvariantCulture);
    }
}
