using Content.Client.Resources;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.Nuke.UI;

public enum KsNukeKeyColor : byte
{
    Beige,
    Yellow,
    Orange,
}

/// <summary>
///     A chunky bevelled key for the nuke panel. It pops out normally, lights up on hover
///     and sinks in while pressed (or while toggled on, like the anchor key).
/// </summary>
public sealed partial class KsNukeKeyButton : ContainerButton
{
    private const string FontPath = "/Fonts/RobotoMono/RobotoMono-Bold.ttf";

    private static readonly Color DisabledTint = Color.FromHex("#A8A595");

    [Dependency] private IResourceCache _resourceCache = default!;

    // Initialized inline: the ContainerButton constructor calls DrawModeChanged before ours runs.
    private readonly Label _label = new()
    {
        Align = Label.AlignMode.Center,
        VAlign = Label.VAlignMode.Center,
    };

    private KsNukeKeyColor _keyColor = KsNukeKeyColor.Beige;
    private int _fontSize;

    public KsNukeKeyButton()
    {
        IoCManager.InjectDependencies(this);
        AddChild(_label);
        FontSize = 14;
    }

    public string? Text
    {
        get => _label.Text;
        set => _label.Text = value;
    }

    public int FontSize
    {
        get => _fontSize;
        set
        {
            _fontSize = value;
            _label.FontOverride = _resourceCache.GetFont(FontPath, value);
        }
    }

    public KsNukeKeyColor KeyColor
    {
        get => _keyColor;
        set
        {
            _keyColor = value;
            UpdateStyle();
        }
    }

    protected override void DrawModeChanged()
    {
        base.DrawModeChanged();
        UpdateStyle();
    }

    private void UpdateStyle()
    {
        var key = (_keyColor, DrawMode);
        if (!Styles.TryGetValue(key, out var style))
        {
            style = BuildStyle(_keyColor, DrawMode);
            Styles.Add(key, style);
        }

        StyleBoxOverride = style.Box;
        _label.FontColorOverride = style.TextColor;
    }

    /// <summary>
    ///     Every key of a colour shares one box per draw mode, the way stylesheet buttons share theirs.
    /// </summary>
    private static readonly Dictionary<(KsNukeKeyColor, DrawModeEnum), (KsBevelStyleBox Box, Color TextColor)> Styles = new();

    private static (KsBevelStyleBox Box, Color TextColor) BuildStyle(KsNukeKeyColor keyColor, DrawModeEnum drawMode)
    {
        var (face, highlight, shadow, hover, text) = keyColor switch
        {
            KsNukeKeyColor.Yellow => ("#9E9A00", "#F2EE0E", "#4F4D00", "#B5B10A", "#1E1D00"),
            KsNukeKeyColor.Orange => ("#C4651C", "#F59A3A", "#62300A", "#D8772C", "#241000"),
            _ => ("#E8E4C9", "#FFFFFF", "#8C8875", "#F7F6EE", "#2F2E26"),
        };

        var box = new KsBevelStyleBox
        {
            Face = Color.FromHex(face),
            Highlight = Color.FromHex(highlight),
            Shadow = Color.FromHex(shadow),
        };
        var textColor = Color.FromHex(text);

        switch (drawMode)
        {
            case DrawModeEnum.Hover:
                box.Face = Color.FromHex(hover);
                break;
            case DrawModeEnum.Pressed:
                (box.Highlight, box.Shadow) = (box.Shadow, box.Highlight);
                break;
            case DrawModeEnum.Disabled:
                box.Face = Color.InterpolateBetween(box.Face, DisabledTint, 0.6f);
                box.Highlight = Color.InterpolateBetween(box.Highlight, DisabledTint, 0.6f);
                box.Shadow = Color.InterpolateBetween(box.Shadow, DisabledTint, 0.6f);
                textColor = Color.InterpolateBetween(textColor, DisabledTint, 0.6f);
                break;
        }

        return (box, textColor);
    }
}
