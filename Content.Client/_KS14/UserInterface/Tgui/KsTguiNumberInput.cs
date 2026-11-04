using System.Numerics;
using Content.Shared.Localizations;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.UserInterface.Tgui;

/// <summary>
///     tgui's NumberInput: a dark field showing the value and unit, with a thin bar on the left showing how far along
///         its range it is. Drag it to change the value, or click it without dragging to type one.
/// </summary>
public sealed partial class KsTguiNumberInput : KsTguiDragNumber
{
    private const float BarWidth = 3f;

    [Dependency] private IResourceCache _resourceCache = default!;

    private readonly Label _display;
    private readonly LineEdit _editor;
    private readonly StyleBoxFlat _box;

    public KsTguiNumberInput()
    {
        IoCManager.InjectDependencies(this);

        MinHeight = KsTguiTheme.ButtonHeight;

        _box = new StyleBoxFlat
        {
            BackgroundColor = KsTguiTheme.InputBackground,
            BorderColor = KsTguiTheme.BorderPrimary,
            BorderThickness = new Thickness(1f),
        };

        _display = new Label
        {
            Align = Label.AlignMode.Right,
            VAlign = Label.VAlignMode.Center,
            Margin = new Thickness(BarWidth + 6f, 0f, 6f, 0f),
            FontOverride = KsTguiTheme.RegularFont(_resourceCache),
            FontColorOverride = KsTguiTheme.Text,
        };

        _editor = new LineEdit
        {
            Visible = false,
            SelectAllOnFocus = true,
            Margin = new Thickness(BarWidth + 2f, 0f, 2f, 0f),
            StyleBoxOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.Black.WithAlpha(0f),
                ContentMarginLeftOverride = 4f,
                ContentMarginRightOverride = 4f,
            },
        };
        _editor.OnTextEntered += _ => CommitEdit();
        _editor.OnFocusExit += _ => CommitEdit();

        AddChild(_display);
        AddChild(_editor);
        ValueUpdated();
    }

    public override bool IsEditing => _editor.Visible;

    protected override void ValueUpdated()
    {
        _display.Text = FormatValue();
    }

    protected override void Clicked()
    {
        _editor.Text = FormatNumber(Value);
        _editor.Visible = true;
        _display.Visible = false;
        _editor.GrabKeyboardFocus();
    }

    private void CommitEdit()
    {
        if (!IsEditing)
            return;

        var text = _editor.Text;
        _editor.Visible = false;
        _display.Visible = true;

        if (UserInputParser.TryFloat(text, out var typed))
            SubmitValue(typed);
        else
            ValueUpdated();
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        _box.BorderColor = IsDragging || IsEditing ? KsTguiTheme.BorderFocused : KsTguiTheme.BorderPrimary;
        _box.Draw(handle, new UIBox2(Vector2.Zero, PixelSize), UIScale);

        var border = MathF.Max(1f, MathF.Floor(UIScale));
        var barHeight = (PixelSize.Y - border * 2f) * Fraction;
        handle.DrawRect(new UIBox2(border, PixelSize.Y - border - barHeight, border + BarWidth * UIScale, PixelSize.Y - border), KsTguiTheme.BorderFocused);
    }
}
