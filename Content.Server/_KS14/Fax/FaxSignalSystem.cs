// SPDX-FileCopyrightText: 2025 Gerkada
// SPDX-FileCopyrightText: 2025 GoobBot
// SPDX-FileCopyrightText: 2025 deltanedas
// SPDX-FileCopyrightText: 2025 deltanedas <@deltanedas:kde.org>
// SPDX-FileCopyrightText: 2025 github_actions[bot]
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.DeviceLinking;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.Fax;
using Content.Shared.Fax.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Fax;

/// <summary>
/// Handles signals for automated fax machines.
/// </summary>
public sealed class FaxSignalSystem : EntitySystem
{
    public static readonly ProtoId<SinkPortPrototype> CopyPort = "FaxCopy";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FaxMachineComponent, SignalReceivedEvent>(OnSignalReceived);
    }

    private void OnSignalReceived(Entity<FaxMachineComponent> ent, ref SignalReceivedEvent args)
    {
        if (args.Port == CopyPort)
            RaiseLocalEvent(ent, new FaxCopyMessage());
    }
}
