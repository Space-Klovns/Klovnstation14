using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._KS14.UserInterface.Tgui;

/// <summary>
///     tgui's Input: a near-black field with a pale blue outline that brightens while focused.
/// </summary>
public sealed class KsTguiInput : LineEdit
{
    private readonly StyleBoxFlat _box = new()
    {
        BackgroundColor = KsTguiTheme.InputBackground,
        BorderColor = KsTguiTheme.BorderPrimary,
        BorderThickness = new Thickness(1f),
        ContentMarginLeftOverride = 4f,
        ContentMarginRightOverride = 4f,
        ContentMarginTopOverride = 1f,
        ContentMarginBottomOverride = 1f,
    };

    public KsTguiInput()
    {
        StyleBoxOverride = _box;
        MinHeight = KsTguiTheme.ButtonHeight;
        OnFocusEnter += _ => _box.BorderColor = KsTguiTheme.BorderFocused;
        OnFocusExit += _ => _box.BorderColor = KsTguiTheme.BorderPrimary;
    }
}
