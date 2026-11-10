using Content.Client._KS14.Tgui.Chat;
using Content.Client.Stylesheets.Fonts;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Content.Client.Stylesheets.StylesheetHelpers;

namespace Content.Client._KS14.Tgui;

public sealed partial class KsTguiSheetlet
{
    /// <summary>
    ///     Rules for the tgui chat panel and say bar.
    /// </summary>
    private void AddChatPanelRules(List<StyleRule> rules)
    {
        // A TextEdit has no font rule of its own, and would be drawn in the engine's default. tgui-say's textarea is 1.1rem.
        var sayFont = IoCManager.Resolve<KsTguiFonts>().GetFont(13);
        rules.Add(E<TextEdit>().Class(KsTguiSayBar.StyleClassEdit).Prop("font", sayFont));

        // tgui-panel's own size for what is in the messages, and 12 for the settings' inputs.
        var fonts = IoCManager.Resolve<KsTguiFonts>();
        rules.Add(E<OutputPanel>().Class(KsTguiChatHeader.StyleClassOutput).Prop("font", fonts.GetFont(13)));
        rules.Add(E<LineEdit>().Class(KsTguiChatSettings.StyleClassInput).Prop("font", fonts.GetFont(12)));
        rules.Add(E<TextEdit>().Class(KsTguiChatSettings.StyleClassInput).Prop("font", fonts.GetFont(12)));
    }
}
