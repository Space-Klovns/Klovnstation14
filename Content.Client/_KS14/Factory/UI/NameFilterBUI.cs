// SPDX-FileCopyrightText: 2025 Gerkada
// SPDX-FileCopyrightText: 2025 GoobBot
// SPDX-FileCopyrightText: 2025 deltanedas <@deltanedas:kde.org>
// SPDX-FileCopyrightText: 2025 github_actions[bot]
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._KS14.Factory.Filters;
using Robust.Client.UserInterface;

namespace Content.Client._KS14.Factory.UI;

public sealed class NameFilterBUI : BoundUserInterface
{
    private NameFilterWindow? _window;

    public NameFilterBUI(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<NameFilterWindow>();
        _window.SetEntity(Owner);
        _window.OnSetName += name => SendPredictedMessage(new NameFilterSetNameMessage(name));
        _window.OnSetMode += mode => SendPredictedMessage(new NameFilterSetModeMessage(mode));
    }
}
