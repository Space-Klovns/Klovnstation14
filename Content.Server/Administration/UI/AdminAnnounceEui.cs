// SPDX-FileCopyrightText: 2021 moonheart08
// SPDX-FileCopyrightText: 2022 Chris V
// SPDX-FileCopyrightText: 2022 Kara
// SPDX-FileCopyrightText: 2022 Veritius
// SPDX-FileCopyrightText: 2022 metalgearsloth
// SPDX-FileCopyrightText: 2022 wrexbe
// SPDX-FileCopyrightText: 2023 Leon Friedrich
// SPDX-FileCopyrightText: 2025 FrauzJ
//
// SPDX-License-Identifier: MIT

using Content.Server.Administration.Managers;
using Content.Server.Chat;
using Content.Server.Chat.Managers;
using Content.Server.Chat.Systems;
using Content.Server.EUI;
using Content.Shared.Administration;
using Content.Shared.Eui;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;

namespace Content.Server.Administration.UI
{
    public sealed class AdminAnnounceEui : BaseEui
    {
        [Dependency] private readonly IAdminManager _adminManager = default!;
        [Dependency] private readonly IChatManager _chatManager = default!;
        private readonly ChatSystem _chatSystem;

        public AdminAnnounceEui()
        {
            IoCManager.InjectDependencies(this);
            _chatSystem = IoCManager.Resolve<IEntitySystemManager>().GetEntitySystem<ChatSystem>();
        }

        public override void Opened()
        {
            StateDirty();
        }

        public override EuiStateBase GetNewState()
        {
            return new AdminAnnounceEuiState();
        }

        public override void HandleMessage(EuiMessageBase msg)
        {
            base.HandleMessage(msg);

            switch (msg)
            {
                case AdminAnnounceEuiMsg.DoAnnounce doAnnounce:
                    if (!_adminManager.HasAdminFlag(Player, AdminFlags.Admin))
                    {
                        Close();
                        break;
                    }

                    switch (doAnnounce.AnnounceType)
                    {
                        case AdminAnnounceType.Server:
                            _chatManager.DispatchServerAnnouncement(doAnnounce.Announcement);
                            break;
                        // TODO: Per-station announcement support
                        case AdminAnnounceType.Station:
                            _chatSystem.DispatchGlobalAnnouncement(doAnnounce.Announcement,
                                doAnnounce.Announcer,
                                colorOverride: Color.Gold,
                                playSound: true,
                                announcementSound: new SoundPathSpecifier("/Audio/_KS14/Announcements/commandreport.ogg"));
                            break;
                        case AdminAnnounceType.Syndicate:
                            _chatSystem.DispatchGlobalAnnouncement(doAnnounce.Announcement,
                                doAnnounce.Announcer,
                                colorOverride: Color.FromHex("#ff0000"),
                                playSound: true,
                                announcementSound: new SoundPathSpecifier("/Audio/_KS14/Announcements/commandreport.ogg"));
                            break;
                        case AdminAnnounceType.Wizard:
                            _chatSystem.DispatchGlobalAnnouncement(doAnnounce.Announcement,
                                doAnnounce.Announcer,
                                colorOverride: Color.FromHex("#ff00ff"));
                            break;
                    }

                    StateDirty();

                    if (doAnnounce.CloseAfter)
                        Close();

                    break;
            }
        }
    }
}
