using System.Numerics;
using Content.Shared._KS14.Hristov;
using Robust.Client.GameObjects;

namespace Content.Client._KS14.Hristov;

public sealed partial class HristovAppearanceSystem : EntitySystem
{
    [Dependency] private SpriteSystem _sprite = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<HristovAppearanceComponent, ComponentStartup>(OnHristovAppearanceAdded);
        SubscribeLocalEvent<HristovAppearanceComponent, ComponentShutdown>(OnHristovAppearanceRemoved);
        SubscribeLocalEvent<HristovAppearanceComponent, AfterAutoHandleStateEvent>(OnHristovAppearanceStateChanged);
    }

    private void OnHristovAppearanceRemoved(Entity<HristovAppearanceComponent> ent, ref ComponentShutdown args)
    {
        RemoveHristovAppearance(ent);
    }

    private void OnHristovAppearanceAdded(Entity<HristovAppearanceComponent> ent, ref ComponentStartup args)
    {
        AddHristovAppearance(ent);
    }

    private void OnHristovAppearanceStateChanged(Entity<HristovAppearanceComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        // After receiving a new state for the component, we remove the old appearance and build a new one.
        // This is so changes to the sprite can be displayed live and allowing them to be edited via ViewVariables.
        RemoveHristovAppearance(ent);
        AddHristovAppearance(ent);
    }

    private void AddHristovAppearance(Entity<HristovAppearanceComponent> ent)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        if (_sprite.LayerMapTryGet((ent, sprite), HristovAppearanceLayer.Base, out var _, false))
            return;

        if (ent.Comp.Sprite == null)
            return;

        var layer = _sprite.AddLayer((ent, sprite), ent.Comp.Sprite);
        _sprite.LayerMapSet((ent, sprite), HristovAppearanceLayer.Base, layer);
        _sprite.LayerSetScale((ent, sprite), layer, ent.Comp.Scale);
    }

    private void RemoveHristovAppearance(Entity<HristovAppearanceComponent> ent)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        if (!_sprite.LayerMapTryGet((ent, sprite), HristovAppearanceLayer.Base, out var layer, false))
            return;

        _sprite.RemoveLayer((ent, sprite), layer);
    }

    private enum HristovAppearanceLayer
    {
        Base,
    }
}
