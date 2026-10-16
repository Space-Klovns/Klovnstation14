using Content.Client.Changelog;
using Content.Client.UserInterface.Systems.EscapeMenu;
using Content.Client.UserInterface.Systems.Guidebook;
using Content.Shared.CCVar;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Collections;
using Robust.Shared.Configuration;

namespace Content.Client.Info
{
    public sealed class LinkBanner : BoxContainer
    {
        private readonly IConfigurationManager _cfg;

        private ValueList<(CVarDef<string> cVar, Button button)> _infoLinks;

        public LinkBanner()
        {
            var buttons = new BoxContainer
            {
                Orientation = LayoutOrientation.Horizontal, /* KS14: added comma */
                StyleClasses = { Content.Client._KS14.Lobby.KsLobbyStyleClasses.KsLobbyMenuButtonSmall } // KS14: flat lobby button style for the children
            };
            AddChild(buttons);

            var uriOpener = IoCManager.Resolve<IUriOpener>();
            _cfg = IoCManager.Resolve<IConfigurationManager>();

            var rulesButton = new Button() {Text = Loc.GetString("ks-lobby-chip-rules" /* KS14: server-info-rules-button -> ks-lobby-chip-rules */)};
            rulesButton.OnPressed += args => new RulesAndInfoWindow().Open();
            buttons.AddChild(rulesButton);

            AddInfoButton("ks-lobby-chip-discord" /* KS14: server-info-discord-button -> ks-lobby-chip-discord */, CCVars.InfoLinksDiscord);
            AddInfoButton("ks-lobby-chip-website" /* KS14: server-info-website-button -> ks-lobby-chip-website */, CCVars.InfoLinksWebsite);
            AddInfoButton("ks-lobby-chip-wiki" /* KS14: server-info-wiki-button -> ks-lobby-chip-wiki */, CCVars.InfoLinksWiki);
            AddInfoButton("ks-lobby-chip-forum" /* KS14: server-info-forum-button -> ks-lobby-chip-forum */, CCVars.InfoLinksForum);
            AddInfoButton("ks-lobby-chip-telegram" /* KS14: server-info-telegram-button -> ks-lobby-chip-telegram */, CCVars.InfoLinksTelegram);

            var guidebookController = UserInterfaceManager.GetUIController<GuidebookUIController>();
            var guidebookButton = new Button() { Text = Loc.GetString("ks-lobby-chip-guidebook" /* KS14: server-info-guidebook-button -> ks-lobby-chip-guidebook */) };
            guidebookButton.OnPressed += _ =>
            {
                guidebookController.ToggleGuidebook();
            };
            buttons.AddChild(guidebookButton);

            var changelogButton = new ChangelogButton() { KsUpperCase = true /* KS14: added */ };
            changelogButton.OnPressed += args => UserInterfaceManager.GetUIController<ChangelogUIController>().ToggleWindow();
            buttons.AddChild(changelogButton);

            void AddInfoButton(string loc, CVarDef<string> cVar)
            {
                var button = new Button { Text = Loc.GetString(loc) };
                button.OnPressed += _ => uriOpener.OpenUri(_cfg.GetCVar(cVar));
                buttons.AddChild(button);
                _infoLinks.Add((cVar, button));
            }
        }

        protected override void EnteredTree()
        {
            // LinkBanner is constructed before the client even connects to the server due to UI refactor stuff.
            // We need to update these buttons when the UI is shown.

            base.EnteredTree();

            foreach (var (cVar, link) in _infoLinks)
            {
                link.Visible = _cfg.GetCVar(cVar) != "";
            }
        }
    }
}
