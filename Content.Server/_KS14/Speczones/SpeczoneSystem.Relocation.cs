// SPDX-FileCopyrightText: 2026 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2026 github_actions[bot]
//
// SPDX-License-Identifier: MPL-2.0

using Content.Shared._KS14.Speczones;

namespace Content.Server._KS14.Speczones;

// This file handles making things with RelocateOnEnteringSpeczone (like ghosts) not able to enter speczones

public sealed partial class SpeczoneSystem : SharedSpeczoneSystem
{
    private void SetupRelocation()
    {
        SubscribeLocalEvent<RelocateOnEnteringSpeczoneComponent, ComponentStartup>(OnRelocatableStartup);
        SubscribeLocalEvent<RelocateOnEnteringSpeczoneComponent, EntParentChangedMessage>(OnRelocatableEntParentChanged);
    }

    private void OnRelocatableStartup(Entity<RelocateOnEnteringSpeczoneComponent> entity, ref ComponentStartup args)
    {
        if (CheckEntityIsInSpeczone(entity, out _))
            _transformSystem.SetCoordinates(entity.Owner, _gameTicker.GetObserverSpawnPoint());
    }

    private void OnRelocatableEntParentChanged(Entity<RelocateOnEnteringSpeczoneComponent> entity, ref EntParentChangedMessage args)
    {
        if (CheckEntityIsInSpeczone(entity, out _))
            _transformSystem.SetCoordinates(entity.Owner, _gameTicker.GetObserverSpawnPoint());
    }
}
