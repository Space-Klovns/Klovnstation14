using Content.Shared._Goobstation.Clothing.Components;
using Content.Shared.Clothing.Components;
using Content.Shared.Inventory.Events;
using Content.Shared.Tag;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Timing;

namespace Content.Shared._Goobstation.Clothing.Systems;

public sealed class ClothingGrantingSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly IComponentFactory _componentFactory = default!;
    [Dependency] private readonly TagSystem _tagSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ClothingGrantComponentComponent, GotEquippedEvent>(OnCompEquip);
        SubscribeLocalEvent<ClothingGrantComponentComponent, GotUnequippedEvent>(OnCompUnequip);

        SubscribeLocalEvent<ClothingGrantTagComponent, GotEquippedEvent>(OnTagEquip);
        SubscribeLocalEvent<ClothingGrantTagComponent, GotUnequippedEvent>(OnTagUnequip);
    }

    private void OnCompEquip(EntityUid uid, ClothingGrantComponentComponent component, GotEquippedEvent args)
    {
        if (!TryComp<ClothingComponent>(uid, out var clothing)) return;

        if (!clothing.Slots.HasFlag(args.SlotFlags)) return;

        // Goobstation
        //if (component.Components.Count > 1)
        //{
        //    Logger.Error("Although a component registry supports multiple components, we cannot bookkeep more than 1 component for ClothingGrantComponent at this time.");
        //    return;
        //}

        // KS14: Made this less abhorrent WTF is wrong with goobcoders?
        if (_gameTiming.ApplyingState)
            return;

        EntityManager.AddComponents(args.EquipTarget, component.Components, removeExisting: false);
        UpdateActivity(args.EquipTarget, component);
    }

    private void OnCompUnequip(EntityUid uid, ClothingGrantComponentComponent component, GotUnequippedEvent args)
    {
        // Goobstation
        //if (!component.IsActive) return;

        // KS14: Made this less abhorrent WTF is wrong with goobcoders?
        if (_gameTiming.ApplyingState)
            return;

        EntityManager.RemoveComponents(args.EquipTarget, component.Components);
        UpdateActivity(args.EquipTarget, component);

        // Goobstation
        //component.IsActive = false;
    }

    private void UpdateActivity(EntityUid equippe, ClothingGrantComponentComponent component)
    {
        foreach (var (name, _) in component.Components)
        {
            var compRegistration = _componentFactory.GetRegistration(name);
            component.Active[name] = HasComp(equippe, compRegistration.Type);
        }
    }

    private void OnTagEquip(EntityUid uid, ClothingGrantTagComponent component, GotEquippedEvent args)
    {
        if (!TryComp<ClothingComponent>(uid, out var clothing))
            return;

        if (!clothing.Slots.HasFlag(args.SlotFlags))
            return;

        EnsureComp<TagComponent>(args.EquipTarget);
        _tagSystem.AddTag(args.EquipTarget, component.Tag);

        component.IsActive = true;
    }

    private void OnTagUnequip(EntityUid uid, ClothingGrantTagComponent component, GotUnequippedEvent args)
    {
        if (!component.IsActive)
            return;

        _tagSystem.RemoveTag(args.EquipTarget, component.Tag);

        component.IsActive = false;
    }
}
