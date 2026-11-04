using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.UserInterface.Tgui;

/// <summary>
///     tgui's LabeledControls: a row of controls spread across the width, each with its label centred underneath.
/// </summary>
public sealed class KsTguiLabeledControls : BoxContainer
{
    public KsTguiLabeledControls()
    {
        Orientation = LayoutOrientation.Horizontal;
        HorizontalExpand = true;
        SeparationOverride = 6;
    }
}

/// <summary>
///     One column of a <see cref="KsTguiLabeledControls"/>. Its children are centred above the label.
/// </summary>
public sealed partial class KsTguiLabeledControlsItem : BoxContainer
{
    [Dependency] private IResourceCache _resourceCache = default!;

    private readonly BoxContainer _content;
    private readonly Robust.Client.UserInterface.Controls.Label _labelCell;

    public KsTguiLabeledControlsItem()
    {
        IoCManager.InjectDependencies(this);

        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        SeparationOverride = 4;

        _content = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            HorizontalAlignment = HAlignment.Center,
            VerticalExpand = true,
            SeparationOverride = 2,
        };

        // Fully qualified: inside this class, Label is the label text property.
        _labelCell = new Robust.Client.UserInterface.Controls.Label
        {
            Align = Robust.Client.UserInterface.Controls.Label.AlignMode.Center,
            FontOverride = KsTguiTheme.RegularFont(_resourceCache),
            FontColorOverride = KsTguiTheme.ToneText(KsTguiTone.Label),
        };

        AddChild(_content);
        AddChild(_labelCell);
        XamlChildren = new KsTguiChildRouter(_content, child => _content.AddChild(child));
    }

    public string? Label
    {
        get => _labelCell.Text;
        set => _labelCell.Text = value;
    }
}
