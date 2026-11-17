using Content.Client._KS14.Tgui;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.UserInterface.Controls;

// Partial so a theme can recolour the box the list's rows draw, which is a stylebox override and so beyond any rule.
public sealed partial class ListContainerButton
{
    /// <summary>
    ///     A style property: the colour of the box behind a row. Without it, the row keeps Baron's maroon.
    /// </summary>
    public const string KsStylePropertyBacking = "ks-backing";

    /// <summary>
    ///     A style property: the colour of the dark rows of a candystripe, which are the first, third and so on of the list.
    ///         Without it a row is never striped, as under Baron.
    /// </summary>
    public const string KsStylePropertyStripe = "ks-list-stripe";

    /// <summary>
    ///     A style property: how far in the row's contents sit (tgui's button padding). Without it, they fill the row.
    /// </summary>
    public const string KsStylePropertyPadding = "ks-list-padding";

    /// <summary>
    ///     On a toggled row. A row also looks pressed (the engine's <c>pressed</c> pseudo-class) while the mouse is held on
    ///         it, which tgui shows differently from selected, and a rule cannot tell the two apart.
    /// </summary>
    public const string KsStyleClassSelected = "KsListRowSelected";

    /// <summary>
    ///     On a row under the mouse. A toggled row looks pressed whether or not it is hovered, and tgui lights a selected
    ///         button on hover.
    /// </summary>
    public const string KsStyleClassHovered = "KsListRowHovered";

    private static readonly Color KsDefaultBacking = Color.FromHex("#7A3038");

    private readonly StyleBoxFlat _ksBacking = new(KsDefaultBacking);

    private KsListRowStripe? _ksStripe;

    /// <summary>
    ///     Whether the row has its dark band, which only a tgui-styled row of an even place in its list does.
    /// </summary>
    public bool KsHasStripe => _ksStripe != null;

    protected override void StylePropertiesChanged()
    {
        base.StylePropertiesChanged();

        _ksBacking.BackgroundColor = TryGetStyleProperty<Color>(KsStylePropertyBacking, out var backing)
            ? backing
            : KsDefaultBacking;

        if (TryGetStyleProperty<Thickness>(KsStylePropertyPadding, out var padding))
        {
            _ksBacking.ContentMarginLeftOverride = padding.Left;
            _ksBacking.ContentMarginTopOverride = padding.Top;
            _ksBacking.ContentMarginRightOverride = padding.Right;
            _ksBacking.ContentMarginBottomOverride = padding.Bottom;
        }
        else
        {
            _ksBacking.ContentMarginLeftOverride = null;
            _ksBacking.ContentMarginTopOverride = null;
            _ksBacking.ContentMarginRightOverride = null;
            _ksBacking.ContentMarginBottomOverride = null;
        }

        var tgui = TryGetStyleProperty<Color>(KsStylePropertyStripe, out var stripe);

        // The row's text colour comes from rules on the row's state, which a ContainerButton does not pass to its children.
        if (tgui)
        {
            foreach (var child in Children)
                child.Restyle();
        }

        var striped = tgui && Index % 2 == 0;
        if (striped)
        {
            if (_ksStripe == null)
            {
                _ksStripe = new KsListRowStripe();
                AddChild(_ksStripe);
                _ksStripe.SetPositionInParent(0);
            }

            _ksStripe.Color = stripe;
            _ksStripe.Bleed = TryGetStyleProperty<Thickness>(KsStylePropertyPadding, out var bleed) ? bleed : default;
        }
        else if (_ksStripe != null)
        {
            _ksStripe.Orphan();
            _ksStripe = null;
        }
    }

    protected override void DrawModeChanged()
    {
        base.DrawModeChanged();

        KsRefreshRowClasses();
    }

    protected override void MouseEntered()
    {
        base.MouseEntered();

        KsRefreshRowClasses();
    }

    protected override void MouseExited()
    {
        base.MouseExited();

        KsRefreshRowClasses();
    }

    private void KsRefreshRowClasses()
    {
        KsSetStyleClass(KsStyleClassSelected, ToggleMode && Pressed);
        KsSetStyleClass(KsStyleClassHovered, IsHovered);
    }

    private void KsSetStyleClass(string styleClass, bool present)
    {
        if (HasStyleClass(styleClass) == present)
            return;

        if (present)
            AddStyleClass(styleClass);
        else
            RemoveStyleClass(styleClass);
    }

    /// <summary>
    ///     The dark band of a candystripe row. A child and not drawn by the row, because the row's own draw is tinted by its
    ///         state (the hover wash), and tgui paints the stripe over the wash in its own colour.
    /// </summary>
    private sealed class KsListRowStripe : Control
    {
        private readonly KsTguiStyleBox _box = new(Color.White, 2f);

        public KsListRowStripe()
        {
            MouseFilter = MouseFilterMode.Ignore;
        }

        public Color Color
        {
            get => _box.BackgroundColor;
            set => _box.BackgroundColor = value;
        }

        /// <summary>
        ///     How far the band reaches past this control on each side: the row's padding, since a row arranges its contents
        ///         inside that and the stripe is the whole row.
        /// </summary>
        public Thickness Bleed;

        protected override void Draw(DrawingHandleScreen handle)
        {
            base.Draw(handle);

            var bleed = Bleed.Scale(UIScale);
            var box = new UIBox2(-bleed.Left, -bleed.Top, PixelWidth + bleed.Right, PixelHeight + bleed.Bottom);
            _box.Draw(handle, box, UIScale);
        }
    }
}
