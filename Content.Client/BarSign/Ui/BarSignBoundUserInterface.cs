// SPDX-FileCopyrightText: 2024 Nemanja
// SPDX-FileCopyrightText: 2025 Pieter-Jan Briers
// SPDX-FileCopyrightText: 2026 github_actions[bot]
// SPDX-FileCopyrightText: 2026 slarticodefast
//
// SPDX-License-Identifier: MIT

using System.Linq;
using Content.Shared.BarSign;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Shared.Prototypes;

namespace Content.Client.BarSign.Ui;

[UsedImplicitly]
public sealed class BarSignBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    [Dependency] private readonly IPrototypeManager _prototype = default!;

    private BarSignMenu? _menu;

    protected override void Open()
    {
        base.Open();

        var allSigns = BarSignSystem.GetAllBarSigns(_prototype)
            .OrderBy(p => Loc.GetString(p.Name))
            .ToList();

        _menu = this.CreateWindow<BarSignMenu>();
        _menu.LoadSigns(allSigns);

        _menu.OnSignSelected += id =>
        {
            SendPredictedMessage(new SetBarSignMessage(id));
        };

        _menu.OnClose += Close;
        _menu.OpenToLeft();
    }

    public override void Update()
    {
        if (!EntMan.TryGetComponent<BarSignComponent>(Owner, out var signComp)
            || !_prototype.Resolve(signComp.Current, out var signPrototype))
            return;

        _menu?.UpdateState(signPrototype);
    }

}

