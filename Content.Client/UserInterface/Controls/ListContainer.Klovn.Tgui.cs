using Robust.Client.Graphics;

namespace Content.Client.UserInterface.Controls;

// Partial so a theme can recolour the box the list's rows draw, which is a stylebox override and so beyond any rule.
public sealed partial class ListContainerButton
{
    /// <summary>
    ///     A style property: the colour of the box behind a row. Without it, the row keeps Baron's maroon.
    /// </summary>
    public const string KsStylePropertyBacking = "ks-backing";

    private static readonly Color KsDefaultBacking = Color.FromHex("#7A3038");

    private readonly StyleBoxFlat _ksBacking = new(KsDefaultBacking);

    protected override void StylePropertiesChanged()
    {
        base.StylePropertiesChanged();

        _ksBacking.BackgroundColor = TryGetStyleProperty<Color>(KsStylePropertyBacking, out var backing)
            ? backing
            : KsDefaultBacking;
    }
}
