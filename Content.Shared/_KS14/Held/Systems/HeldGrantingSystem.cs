// SPDX-FileCopyrightText: 2024 Aidenkrz
// SPDX-FileCopyrightText: 2024 Piras314
// SPDX-FileCopyrightText: 2024 VMSolidus
// SPDX-FileCopyrightText: 2025 Aiden
// SPDX-FileCopyrightText: 2025 GoobBot
// SPDX-FileCopyrightText: 2025 Ilya246
// SPDX-FileCopyrightText: 2025 Misandry
// SPDX-FileCopyrightText: 2025 gus
// SPDX-FileCopyrightText: 2026 nabegator220
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._KS14.Held.Components;
using Content.Shared.Hands;
using Content.Shared.Tag;
using Robust.Shared.Serialization.Manager;

namespace Content.Shared._KS14.Held.Systems;
//TAG SHIT IS UNTESTED HERE BE DRAGONS
public sealed class HeldGrantingSystem : EntitySystem
{
    [Dependency] private readonly IComponentFactory _componentFactory = default!;
    [Dependency] private readonly ISerializationManager _serializationManager = default!;
    [Dependency] private readonly TagSystem _tagSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<HeldGrantComponentComponent, GotEquippedHandEvent>(OnCompPickup);
        SubscribeLocalEvent<HeldGrantComponentComponent, GotUnequippedHandEvent>(OnCompDrop);

        SubscribeLocalEvent<HeldGrantTagComponent, GotEquippedHandEvent>(OnTagPickup);
        SubscribeLocalEvent<HeldGrantTagComponent, GotUnequippedHandEvent>(OnTagDrop);
    }

    private void OnCompPickup(EntityUid uid, HeldGrantComponentComponent component, GotEquippedHandEvent args)
    {
        var holder = args.User;

        foreach (var (name, data) in component.Components)
        {
            var newComp = (Component) _componentFactory.GetComponent(name);

            if (HasComp(holder, newComp.GetType()))
                continue;

            var temp = (object) newComp;
            _serializationManager.CopyTo(data.Component, ref temp);
            EntityManager.AddComponent(holder, (Component)temp!);

            component.Active[name] = true;
        }
    }

    private void OnCompDrop(EntityUid uid, HeldGrantComponentComponent component, GotUnequippedHandEvent args)
    {
        var holder = args.User;

        foreach (var (name, data) in component.Components)
        {
            if (!component.Active.ContainsKey(name) || !component.Active[name])
                continue;

            var newComp = (Component) _componentFactory.GetComponent(name);

            RemComp(holder, newComp.GetType());
            component.Active[name] = false;
        }
    }

    private void OnTagPickup(EntityUid uid, HeldGrantTagComponent component, GotEquippedHandEvent args) //UNTESTED
    {
        var holder = args.User;

        EnsureComp<TagComponent>(holder);
        _tagSystem.AddTag(holder, component.Tag);

        component.IsActive = true;
    }

    private void OnTagDrop(EntityUid uid, HeldGrantTagComponent component, GotUnequippedHandEvent args) //UNTESTED
    {
        if (!component.IsActive)
            return;

        var holder = args.User;

        _tagSystem.RemoveTag(holder, component.Tag);

        component.IsActive = false;
    }
}
