using Content.Shared.CCVar;
using Content.Shared.Chat;
using Content.Shared.Communications;
using Robust.Client.UserInterface;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Client.Communications.UI
{
    public sealed class CommunicationsConsoleBoundUserInterface : BoundUserInterface
    {
        [Dependency] private readonly IConfigurationManager _cfg = default!;

        [ViewVariables]
        private CommunicationsConsoleMenu? _menu;

        public CommunicationsConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
        {
        }

        protected override void Open()
        {
            base.Open();

            _menu = this.CreateWindow<CommunicationsConsoleMenu>();
            _menu.OnAnnounce += AnnounceButtonPressed;
            _menu.OnBroadcast += BroadcastButtonPressed;
            _menu.OnAlertLevel += AlertLevelSelected;
            _menu.OnEmergencyLevel += EmergencyShuttleButtonPressed; // KS14: Evac reason text
        }

        public void AlertLevelSelected(string level)
        {
            if (_menu!.AlertLevelSelectable)
            {
                _menu.CurrentLevel = level;
                SendMessage(new CommunicationsConsoleSelectAlertLevelMessage(level));
            }
        }

        public void EmergencyShuttleButtonPressed(string message) // KS14: Evac reason text
        {
            if (_menu!.CountdownStarted)
                RecallShuttle(message); // KS14: Evac reason text
            else
                CallShuttle(message); // KS14: Evac reason text
        }

        public void AnnounceButtonPressed(string message)
        {
            var maxLength = _cfg.GetCVar(CCVars.ChatMaxAnnouncementLength);
            var msg = SharedChatSystem.SanitizeAnnouncement(message, maxLength);
            SendMessage(new CommunicationsConsoleAnnounceMessage(msg));
        }

        public void BroadcastButtonPressed(string message)
        {
            SendMessage(new CommunicationsConsoleBroadcastMessage(message));
        }

        public void CallShuttle(string message) // KS14: Evac reason text
        {
            var maxLength = _cfg.GetCVar(CCVars.ChatMaxAnnouncementLength); // KS14: Evac reason text
            var msg = SharedChatSystem.SanitizeAnnouncement(message, maxLength); // KS14: Evac reason text

            SendMessage(new CommunicationsConsoleCallEmergencyShuttleMessage(msg)); // KS14: Evac reason text
        }

        public void RecallShuttle(string message) // KS14: Evac reason text
        {
            var maxLength = _cfg.GetCVar(CCVars.ChatMaxAnnouncementLength); // KS14: Evac reason text
            var msg = SharedChatSystem.SanitizeAnnouncement(message, maxLength); // KS14: Evac reason text

            SendMessage(new CommunicationsConsoleRecallEmergencyShuttleMessage(msg)); // KS14: Evac reason text
        }

        protected override void UpdateState(BoundUserInterfaceState state)
        {
            base.UpdateState(state);

            if (state is not CommunicationsConsoleInterfaceState commsState)
                return;

            if (_menu != null)
            {
                _menu.CanAnnounce = commsState.CanAnnounce;
                _menu.CanBroadcast = commsState.CanBroadcast;
                _menu.CanCall = commsState.CanCall;
                _menu.CountdownStarted = commsState.CountdownStarted;
                _menu.AlertLevelSelectable = commsState.AlertLevels != null && !float.IsNaN(commsState.CurrentAlertDelay) && commsState.CurrentAlertDelay <= 0;
                _menu.CurrentLevel = commsState.CurrentAlert;
                _menu.CountdownEnd = commsState.ExpectedCountdownEnd;

                _menu.UpdateCountdown();
                _menu.UpdateAlertLevels(commsState.AlertLevels, _menu.CurrentLevel);
                _menu.AlertLevelButton.Disabled = !_menu.AlertLevelSelectable;
                _menu.EmergencyShuttleButton.Disabled = !_menu.CanCall;
                _menu.AnnounceButton.Disabled = !_menu.CanAnnounce;
                _menu.BroadcastButton.Disabled = !_menu.CanBroadcast;
            }
        }
    }
}
