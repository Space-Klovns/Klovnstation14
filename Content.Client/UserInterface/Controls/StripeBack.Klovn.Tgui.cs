namespace Content.Client.UserInterface.Controls;

// Partial so the tgui theme, whose notice boxes have no edge lines, can turn off the hardcoded ones.
public sealed partial class StripeBack
{
    /// <summary>
    ///     A style property: when true, no edge lines are drawn or made room for, whatever the edge settings say.
    /// </summary>
    public const string KsStylePropertyFlat = "ks-flat";

    private bool KsFlat => TryGetStyleProperty<bool>(KsStylePropertyFlat, out var flat) && flat;

    private bool KsDrawTopEdge => HasTopEdge && !KsFlat;

    private bool KsDrawBottomEdge => HasBottomEdge && !KsFlat;
}
