// SPDX-FileCopyrightText: 2025 Gerkada
// SPDX-FileCopyrightText: 2025 GoobBot
// SPDX-FileCopyrightText: 2025 deltanedas
// SPDX-FileCopyrightText: 2025 deltanedas <@deltanedas:kde.org>
// SPDX-FileCopyrightText: 2025 gluesniffler
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._KS14.Factory;
using Content.Server.Construction.Components;

namespace Content.Server._KS14.Factory;

public sealed class InteractorSystem : SharedInteractorSystem
{
    private EntityQuery<ConstructionComponent> _constructionQuery;

    public override void Initialize()
    {
        base.Initialize();

        _constructionQuery = GetEntityQuery<ConstructionComponent>();

        SubscribeLocalEvent<InteractorComponent, MachineStartedEvent>(OnStarted);
    }

    private void OnStarted(Entity<InteractorComponent> ent, ref MachineStartedEvent args)
    {
        // nothing there or another doafter is already running
        var count = ent.Comp.TargetEntities.Count;
        if (count == 0 || HasDoAfter(ent))
        {
            Machine.Failed(ent.Owner);
            return;
        }

        var i = count - 1;
        var netEnt = ent.Comp.TargetEntities[i].Item1;
        var target = GetEntity(netEnt);
        _constructionQuery.TryComp(target, out var construction);
        var originalCount = construction?.InteractionQueue?.Count ?? 0;
        if (!InteractWith(ent, target))
        {
            // have to remove it since user's filter was bad due to unhandled interaction
            RemoveTarget(ent, target);
            Machine.Failed(ent.Owner);
            return;
        }

        // construction supercode queues it instead of starting a doafter now, assume that queuing means it has started
        var newCount = construction?.InteractionQueue?.Count ?? 0;
        if (newCount > originalCount
            || HasDoAfter(ent))
        {
            Machine.Started(ent.Owner);
            UpdateAppearance(ent, InteractorState.Active);
        }
        else
        {
            // no doafter, complete it immediately
            TryRemoveTarget(ent, target);
            Machine.Completed(ent.Owner);
            UpdateAppearance(ent);
        }
    }
}
