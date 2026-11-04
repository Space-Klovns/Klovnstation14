using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.UserInterface.Tgui;

/// <summary>
///     tgui's ProgressBar: a 1px outline and a fill in the tone's background shade, with the percentage right-aligned
///         over it.
/// </summary>
public sealed partial class KsTguiProgressBar : Control
{
    [Dependency] private IResourceCache _resourceCache = default!;

    private readonly Label _label;
    private float _value;
    private KsTguiTone _tone;
    private Color _fillColor = KsTguiTheme.Background(KsTguiTheme.Primary);

    public KsTguiProgressBar()
    {
        IoCManager.InjectDependencies(this);

        MinHeight = KsTguiTheme.ButtonHeight;
        HorizontalExpand = true;

        _label = new Label
        {
            Align = Label.AlignMode.Right,
            VAlign = Label.VAlignMode.Center,
            Margin = new Thickness(6f, 0f),
            FontOverride = KsTguiTheme.RegularFont(_resourceCache),
            FontColorOverride = KsTguiTheme.Text,
        };
        AddChild(_label);
        Value = 0f;
    }

    /// <summary>
    ///     How full the bar is, from 0 to 1.
    /// </summary>
    public float Value
    {
        get => _value;
        set
        {
            _value = Math.Clamp(value, 0f, 1f);
            _label.Text = Loc.GetString("ks-tgui-percent", ("percent", MathF.Round(_value * 100f)));
        }
    }

    public KsTguiTone Tone
    {
        get => _tone;
        set
        {
            _tone = value;
            _fillColor = KsTguiTheme.Background(KsTguiTheme.Token(value));
        }
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var box = new UIBox2(Vector2.Zero, PixelSize);
        var border = MathF.Max(1f, MathF.Floor(UIScale));

        handle.DrawRect(new UIBox2(box.Left, box.Top, box.Left + box.Width * _value, box.Bottom), _fillColor);
        handle.DrawRect(new UIBox2(box.Left, box.Top, box.Right, box.Top + border), _fillColor);
        handle.DrawRect(new UIBox2(box.Left, box.Bottom - border, box.Right, box.Bottom), _fillColor);
        handle.DrawRect(new UIBox2(box.Left, box.Top, box.Left + border, box.Bottom), _fillColor);
        handle.DrawRect(new UIBox2(box.Right - border, box.Top, box.Right, box.Bottom), _fillColor);
    }
}
