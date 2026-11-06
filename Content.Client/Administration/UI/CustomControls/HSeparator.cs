using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client.Administration.UI.CustomControls;

public sealed partial class HSeparator : Control
{
    //KS14 - needed for rmc vends (and the tgui section line)
    private PanelContainer _panel;

    public static readonly Color DefaultSeparatorColor = Color.FromHex("#3D4059");

    public Color Color
    {
        get => ((StyleBoxFlat)_panel.PanelOverride!).BackgroundColor;
        set => ((StyleBoxFlat)_panel.PanelOverride!).BackgroundColor = value;
    }

    public HSeparator()
    {
        _panel = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = DefaultSeparatorColor,
                ContentMarginBottomOverride = 2,
                ContentMarginLeftOverride = 2
            }
        };

        AddChild(_panel);
        _defaultBox = (StyleBox)_panel.PanelOverride!;
    }

    private readonly StyleBox _defaultBox;
    private StyleBox? _sectionLineBox;

    protected override void StylePropertiesChanged()
    {
        base.StylePropertiesChanged();

        // The tgui theme turns a separator carrying the KsTguiSectionLine class into a section's 2px underline.
        if (TryGetStyleProperty<bool>(Content.Client._KS14.Tgui.KsTguiSheetlet.StylePropertySectionLine, out var line) && line)
            _panel.PanelOverride = _sectionLineBox ??= new StyleBoxFlat
            {
                BackgroundColor = Content.Client._KS14.Tgui.KsTguiPalettes.PrimaryBase,
                ContentMarginBottomOverride = 2,
            };
        else
            _panel.PanelOverride = _defaultBox;
    }
    //KS14 end
}
