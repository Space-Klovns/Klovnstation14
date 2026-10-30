using Content.Client.Changelog;
using Content.Client.Credits;
using Content.Shared.CCVar;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Utility;

namespace Content.Client.Info
{
    public sealed class DevInfoBanner : BoxContainer
    {
        public DevInfoBanner() {
            var buttons = new BoxContainer
            {
                Orientation = LayoutOrientation.Horizontal, /* KS14: added comma */
                StyleClasses = { Content.Client._KS14.Lobby.KsLobbyStyleClasses.KsLobbyMenuButtonSmall } // KS14: flat lobby button style for the children
            };
            AddChild(buttons);

            var uriOpener = IoCManager.Resolve<IUriOpener>();
            var cfg = IoCManager.Resolve<IConfigurationManager>();

            var bugReport = cfg.GetCVar(CCVars.InfoLinksBugReport);
            if (bugReport != "")
            {
                var reportButton = new Button {Text = Loc.GetString("ks-lobby-chip-report" /* KS14: server-info-report-button -> ks-lobby-chip-report */)};
                reportButton.OnPressed += args => uriOpener.OpenUri(bugReport);
                buttons.AddChild(reportButton);
            }

            var creditsButton = new Button {Text = Loc.GetString("ks-lobby-chip-credits" /* KS14: server-info-credits-button -> ks-lobby-chip-credits */)};
            creditsButton.OnPressed += args => new CreditsWindow().Open();
            buttons.AddChild(creditsButton);
        }
    }
}
