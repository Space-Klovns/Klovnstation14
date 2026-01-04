// SPDX-FileCopyrightText: 2026 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2026 github_actions[bot]
//
// SPDX-License-Identifier: MPL-2.0

using Content.Shared.Chat;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Standing;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Player;

namespace Content.Shared._KS14.EmoteOnHit;

public sealed class SharedFlipOnHitSystem : EntitySystem
{
    [Dependency] private readonly ItemToggleSystem _itemToggleSystem = default!;
    [Dependency] private readonly StandingStateSystem _standingStateSystem = default!;
    [Dependency] private readonly SharedChatSystem _chatSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<EmoteOnHitComponent, MeleeHitEvent>(OnHit);
    }

    private void OnHit(Entity<EmoteOnHitComponent> entity, ref MeleeHitEvent args)
    {
        if (args.HitEntities.Count == 0 ||
            !_itemToggleSystem.IsActivated(entity.Owner))
            return;

        if (entity.Comp.RequireStanding &&
            _standingStateSystem.IsDown(args.User))
            return;

        // Networked to everyone except for the user; to predict
        _chatSystem.TryEmoteWithoutChat(args.User, entity.Comp.Emote,
            networkedFilter: Filter.PvsExcept(args.User, entityManager: EntityManager));
    }
}
