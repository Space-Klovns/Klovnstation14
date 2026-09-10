using System.Linq;
using Content.Shared._KS14.Anchorless.Components;
using Robust.Client.GameObjects;
using Robust.Shared.Utility;

namespace Content.Client._KS14.Anchorless.Systems;

/// <summary>Renders the public, replicated Anchorless horror form visual.</summary>
public sealed partial class AnchorlessHorrorVisualSystem : EntitySystem
{
    [Dependency] private SpriteSystem _sprite = default!;

    private readonly Dictionary<EntityUid, List<bool>> _hiddenLayers = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<AnchorlessHorrorVisualComponent, ComponentStartup>(OnVisualStartup);
        SubscribeLocalEvent<AnchorlessHorrorVisualComponent, ComponentShutdown>(OnVisualShutdown);
        SubscribeLocalEvent<AnchorlessHorrorVisualComponent, AfterAutoHandleStateEvent>(OnVisualStateChanged);
    }

    private void OnVisualStartup(Entity<AnchorlessHorrorVisualComponent> ent, ref ComponentStartup args)
    {
        UpdateVisual(ent);
    }

    private void OnVisualShutdown(Entity<AnchorlessHorrorVisualComponent> ent, ref ComponentShutdown args)
    {
        RemoveVisual(ent);
    }

    private void OnVisualStateChanged(Entity<AnchorlessHorrorVisualComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        UpdateVisual(ent);
    }

    private void UpdateVisual(Entity<AnchorlessHorrorVisualComponent> ent)
    {
        if (ent.Comp.HorrorForm)
            AddVisual(ent);
        else
            RemoveVisual(ent);
    }

    private void AddVisual(Entity<AnchorlessHorrorVisualComponent> ent)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite) ||
            _sprite.LayerMapTryGet((ent, sprite), HorrorVisualLayer.Key, out _, false))
            return;

        _hiddenLayers[ent.Owner] = sprite.AllLayers.Select(layer => layer.Visible).ToList();
        for (var i = 0; i < _hiddenLayers[ent.Owner].Count; i++)
            _sprite.LayerSetVisible((ent, sprite), i, false);

        var layer = _sprite.AddLayer((ent, sprite),
            new SpriteSpecifier.Rsi(ent.Comp.HorrorSprite, ent.Comp.HorrorSpriteState));
        _sprite.LayerSetScale((ent, sprite), layer, ent.Comp.HorrorScale);
        _sprite.LayerMapSet((ent, sprite), HorrorVisualLayer.Key, layer);
    }

    private void RemoveVisual(Entity<AnchorlessHorrorVisualComponent> ent)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        if (_sprite.LayerMapTryGet((ent, sprite), HorrorVisualLayer.Key, out var layer, false))
            _sprite.RemoveLayer((ent, sprite), layer);

        if (_hiddenLayers.Remove(ent.Owner, out var visibleLayers))
            for (var i = 0; i < visibleLayers.Count; i++)
                _sprite.LayerSetVisible((ent, sprite), i, visibleLayers[i]);
    }

    private enum HorrorVisualLayer
    {
        Key,
    }
}
