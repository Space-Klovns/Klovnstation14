using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.UserInterface.Tgui;

/// <summary>
///     tgui's LabeledList: a two-column table of greyish-blue "Label:" cells and their contents, the label column as
///         wide as its widest label. Each <see cref="KsTguiLabeledListItem"/> brings its own label cell with it.
/// </summary>
public sealed class KsTguiLabeledList : GridContainer
{
    public KsTguiLabeledList()
    {
        Columns = 2;
        // tgui pads every cell 3px 6px, so cells sit 12px apart across and 6px apart down.
        HSeparationOverride = 12;
        VSeparationOverride = 6;
        XamlChildren = new KsTguiChildRouter(this, AddRouted);
    }

    public void AddItem(KsTguiLabeledListItem item)
    {
        AddChild(item.LabelCell);
        AddChild(item);
    }

    private void AddRouted(Control child)
    {
        if (child is KsTguiLabeledListItem item)
        {
            AddItem(item);
            return;
        }

        // Anything else spans nothing: give it an empty label cell so the columns stay paired.
        AddChild(new Control());
        AddChild(child);
    }
}

/// <summary>
///     One row of a <see cref="KsTguiLabeledList"/>. Its children are the row's content, laid out left to right.
/// </summary>
public sealed partial class KsTguiLabeledListItem : BoxContainer
{
    [Dependency] private IResourceCache _resourceCache = default!;

    private string? _label;

    public KsTguiLabeledListItem()
    {
        IoCManager.InjectDependencies(this);

        Orientation = LayoutOrientation.Horizontal;
        HorizontalExpand = true;
        SeparationOverride = 4;

        // Fully qualified: inside this class, Label is the label text property.
        LabelCell = new Robust.Client.UserInterface.Controls.Label
        {
            MinWidth = 60f,
            VAlign = Robust.Client.UserInterface.Controls.Label.VAlignMode.Center,
            FontOverride = KsTguiTheme.RegularFont(_resourceCache),
            FontColorOverride = KsTguiTheme.ToneText(KsTguiTone.Label),
        };
    }

    /// <summary>
    ///     The label column's cell. The list adds it next to this row.
    /// </summary>
    public Robust.Client.UserInterface.Controls.Label LabelCell { get; }

    /// <summary>
    ///     The label, shown with a colon after it as tgui does.
    /// </summary>
    public string? Label
    {
        get => _label;
        set
        {
            _label = value;
            LabelCell.Text = string.IsNullOrEmpty(value) ? null : Loc.GetString("ks-tgui-labeled-list-label", ("label", value));
        }
    }

    /// <summary>
    ///     tgui's <c>labelColor</c>. The mixer's node rows use green and blue.
    /// </summary>
    public Color LabelColor
    {
        get => LabelCell.FontColorOverride ?? KsTguiTheme.ToneText(KsTguiTone.Label);
        set => LabelCell.FontColorOverride = value;
    }

    protected override void VisibilityChanged(bool newVisible)
    {
        base.VisibilityChanged(newVisible);
        LabelCell.Visible = newVisible;
    }
}
