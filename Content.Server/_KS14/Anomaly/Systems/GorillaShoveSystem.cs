// SPDX-FileCopyrightText: 2025 Gerkada
// SPDX-FileCopyrightText: 2025 github_actions[bot]
//
// SPDX-License-Identifier: MIT

using Content.Server.Disposal.Unit;
using Content.Shared.Disposal.Components;
using Content.Server.Popups;
using Content.Shared.Anomaly.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared._KS14.Anomaly.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.GameObjects;
using Content.Shared.Item;
using System.Linq;

namespace Content.Server._KS14.Anomaly.Systems;

public sealed class GorillaShoveSystem : EntitySystem
{
    [Dependency] private readonly DisposalUnitSystem _disposals = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        // Subscribe to MeleeHitEvent
        SubscribeLocalEvent<GorillaGauntletComponent, MeleeHitEvent>(OnMeleeHit);
    }

    private void OnMeleeHit(Entity<GorillaGauntletComponent> ent, ref MeleeHitEvent args)
    {
        // Basic Validation
        if (args.Handled || args.HitEntities.Count == 0)
            return;

        var user = args.User;
        // We only care about the first thing hit (you can't shove 2 anomalies at once)
        var target = args.HitEntities.First();

        // Check target
        if (!TryComp<GorillaDisposalComponent>(target, out _))
            return;

        // Check for Core in Gauntlet
        var slotId = "core_slot";
        if (!_itemSlots.TryGetSlot(ent.Owner, slotId, out var slot) || slot.Item is not { } coreItem)
        {
            _popup.PopupEntity(Loc.GetString("gorilla-shove-gauntlet-not-active"), user, user);
            return;
        }

        // Verify it is an Anomaly Core
        if (!TryComp<AnomalyCoreComponent>(coreItem, out var coreComp))
        {
            _popup.PopupEntity(Loc.GetString("gorilla-shove-gauntlet-not-active"), user, user);
            return;
        }

        // Check Charge logic:
        // If the core is decayed, it has limited charges. If it is 0, we can't use it.
        if (coreComp.IsDecayed && coreComp.Charge <= 0)
        {
            _popup.PopupEntity(Loc.GetString("gorilla-shove-gauntlet-not-active"), user, user);
            return;
        }

        // Find nearby disposal unit
        var xform = Transform(target);
        var disposals = _lookup.GetEntitiesInRange<DisposalUnitComponent>(xform.Coordinates, 1.5f);
        var targetDisposal = disposals.FirstOrDefault();

        if (targetDisposal == null)
        {
            // No charge consumed yet, just return
            _popup.PopupEntity(Loc.GetString("gorilla-shove-not-disposals"), user, user);
            return;
        }

        //Prepare entity
        if (xform.Anchored)
        {
            _transform.Unanchor(target, xform);
        }

        //Force Dynamic Physics
        if (TryComp<PhysicsComponent>(target, out var body))
        {
            _physics.SetBodyType(target, BodyType.Dynamic, body: body);
            _physics.SetCanCollide(target, true, body: body);
            _physics.WakeBody(target, body: body);
        }

        // Add ItemComponent
        // This makes the Anomaly "look" like trash to the Disposal Unit logic.
        EnsureComp<ItemComponent>(target);


        // Teleport to Unit
        // We put it exactly on top of the unit so TryInsert never fails the reach check.
        var disposalXform = Transform(targetDisposal.Owner);
        _transform.SetCoordinates(target, xform, disposalXform.Coordinates);

        //Try insert
        // Now that it's an "Item" and "Dynamic", standard TryInsert should work.
        // We pass 'null' for user to bypass "Player Reach" checks.
        if (!_disposals.TryInsert(targetDisposal.Owner, target, null))
        {
            // If this fails, something is fundamentally broken
            _popup.PopupEntity("Shove failed! (Unit blocked/full?)", user, user);
            return;
        }

        // Success
        if (coreComp.IsDecayed)
        {
            coreComp.Charge--;
            Dirty(coreItem, coreComp);
        }

        args.Handled = true;
    }
}
