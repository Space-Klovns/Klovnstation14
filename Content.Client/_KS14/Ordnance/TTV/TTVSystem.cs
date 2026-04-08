using Content.Server._KS14.Ordnance.TTV;
using Content.Shared._KS14.Ordnance.TTV;
using Content.Shared.Clothing.Components;
using Content.Shared.Clothing.EntitySystems;
using Content.Shared.Containers.ItemSlots;
using Robust.Client.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Utility;

namespace Content.Client._KS14.Ordnance.TTV;

public sealed class TTVSystem : SharedTTVSystem
{
    [Dependency] private readonly SpriteSystem _spriteSystem = default!;
    [Dependency] private readonly ClothingSystem _clothingSystem = default!;


    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<TTVComponent, ComponentInit>(TTVInit);
        SubscribeLocalEvent<TTVComponent, AppearanceChangeEvent>(OnAppearanceChange);

        SubscribeLocalEvent<TTVComponent, EntInsertedIntoContainerMessage>(OnTankInserted);
        SubscribeLocalEvent<TTVComponent, EntRemovedFromContainerMessage>(OnTankRemoved);
    }

    private void TTVInit(Entity<TTVComponent> ttv, ref ComponentInit args)
    {
        var ttvSpriteEntity = new Entity<SpriteComponent?>(ttv, null);
        if (!Resolve(ttvSpriteEntity, ref ttvSpriteEntity.Comp))
            return;

        if (!TryComp<ItemSlotsComponent>(ttv, out var slotsComponent))
            return;

        foreach (var (slotId, _) in slotsComponent.Slots)
            _spriteSystem.LayerMapReserve(ttvSpriteEntity, slotId);
    }

    private void OnAppearanceChange(Entity<TTVComponent> ttv, ref AppearanceChangeEvent args)
        => UpdateAppearance((ttv.Owner, null, null), clothingMapKey: ttv.Comp.ClothingMapKey);

    // I didn't use `Entity<TTVComponent` for this because of nullability trolling.
    public void UpdateAppearance(EntityUid ttvUid)
    {
        TTVComponent? ttvComponent = null;
        if (!Resolve(ttvUid, ref ttvComponent, logMissing: false))
            return;

        UpdateAppearance((ttvUid, null, null), clothingMapKey: ttvComponent.ClothingMapKey);
    }

    private void UpdateAppearance(Entity<ItemSlotsComponent?, SpriteComponent?> ttv, string clothingMapKey)
    {
        if (!Resolve(ttv, ref ttv.Comp1, logMissing: false) || !Resolve(ttv, ref ttv.Comp2, logMissing: false))
            return;

        var slotsComponent = ttv.Comp1;
        var ttvSpriteEntity = new Entity<SpriteComponent?>(ttv, ttv.Comp2);

        var i = 0;
        var retrTtvSpriteArray = new[] { false, false };

        foreach (var (slotId, slot) in slotsComponent.Slots)
        {
            var slotOccupied = slot.HasItem;
            _spriteSystem.LayerSetVisible(ttvSpriteEntity, slotId, slotOccupied);

            if (!slotOccupied || !TTVCompatibleQuery.TryComp(slot.Item, out var itemCompatibleComponent))
                continue;

            _spriteSystem.LayerSetSprite(ttvSpriteEntity, slotId, new SpriteSpecifier.Rsi(itemCompatibleComponent.InsertedTexture!.Value, itemCompatibleComponent.InsertedState));

            //
            if (i < retrTtvSpriteArray.Length)
                retrTtvSpriteArray[i] |= true;

            ++i;
        }

        var ttvUid = ttv.Owner;
        if (!TryComp<ClothingComponent>(ttvUid, out var clothingComponent))
            return;

        foreach (var slot in clothingComponent.ClothingVisuals)
        {
            // Possible combinations must be: `ttv`, `ttvl`, `ttvr`, `ttvlr`.
            _clothingSystem.SetEquippedPrefix(ttvUid, "ttv" +
                (retrTtvSpriteArray[0] ? "l" : "") +
                (retrTtvSpriteArray[1] ? "r" : ""), clothingComponent);
        }
    }

    private void OnTankInserted(Entity<TTVComponent> ttv, ref EntInsertedIntoContainerMessage _)
        => UpdateAppearance(ttv);

    private void OnTankRemoved(Entity<TTVComponent> ttv, ref EntRemovedFromContainerMessage _)
        => UpdateAppearance(ttv);
}
