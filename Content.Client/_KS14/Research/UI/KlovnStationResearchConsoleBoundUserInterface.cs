// SPDX-FileCopyrightText: 2025 Aiden
// SPDX-FileCopyrightText: 2025 FaDeOkno
// SPDX-FileCopyrightText: 2025 Gerkada
// SPDX-FileCopyrightText: 2025 coderabbitai[bot]
// SPDX-FileCopyrightText: 2025 github_actions[bot]
// SPDX-FileCopyrightText: 2025 gluesniffler
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using Content.Client._KS14.Research;
using Content.Shared.Research.Components;
using Content.Shared._KS14.Research.Components;
using Content.Shared.Research.Prototypes;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Shared.Prototypes;

namespace Content.Client._KS14.Research.UI;

[UsedImplicitly]
public sealed class KlovnStationResearchConsoleBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private FancyResearchConsoleMenu? _consoleMenu;

    public KlovnStationResearchConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        var owner = Owner;

        _consoleMenu = this.CreateWindow<FancyResearchConsoleMenu>();
        _consoleMenu.SetEntity(owner);
        _consoleMenu.OnClose += () =>
        {
            _consoleMenu = null;
        };

        _consoleMenu.OnTechnologyCardPressed += id =>
        {
            SendMessage(new ConsoleUnlockTechnologyMessage(id));
        };

        _consoleMenu.OnServerButtonPressed += () =>
        {
            SendMessage(new ConsoleServerSelectionMessage());
        };
    }

    public override void OnProtoReload(PrototypesReloadedEventArgs args)
    {
        base.OnProtoReload(args);

        if (!args.WasModified<TechnologyPrototype>())
            return;

        if (State is not ResearchConsoleBoundInterfaceState rState)
            return;

        _consoleMenu?.UpdatePanels(rState.Researches);
        _consoleMenu?.UpdateInformationPanel(rState.Points);
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not ResearchConsoleBoundInterfaceState castState)
            return;

        if (_consoleMenu == null)
            return;

        // Efficiently update without full panel rebuilds
        if (!_consoleMenu.List.SequenceEqual(castState.Researches))
        {
            _consoleMenu.UpdatePanels(castState.Researches);
        }
        else if (_consoleMenu.Points != castState.Points) // Only update points if techs haven't changed
        {
            _consoleMenu.UpdateInformationPanel(castState.Points);
        }
    }
}
