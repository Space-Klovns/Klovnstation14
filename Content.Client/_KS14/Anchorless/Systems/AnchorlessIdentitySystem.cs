using System.Linq;
using System.Numerics;
using Content.Shared._KS14.Anchorless.Components;
using Content.Shared._KS14.Anchorless.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Robust.Client.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Utility;

namespace Content.Client._KS14.Anchorless.Systems;

public sealed partial class AnchorlessIdentitySystem : SharedAnchorlessIdentitySystem
{
    [Dependency] private MovementSpeedModifierSystem _movement = default!;
    [Dependency] private SpriteSystem _sprite = default!;
    private readonly Dictionary<EntityUid, List<bool>> _hiddenLayers = new();
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<AnchorlessComponent, ComponentHandleState>(OnHandleState);
    }

    private void OnHandleState(Entity<AnchorlessComponent> ent, ref ComponentHandleState args)
    {
        if (args.Current is not AnchorlessIdentityComponentState state)
            return;

        ent.Comp.LearnedIdentities = state.LearnedIdentities.Select(identity => new AnchorlessIdentityData
        {
            StoredIdentity = EnsureEntity<AnchorlessComponent>(identity.StoredIdentity, ent),
            OriginalEntity = EnsureEntity<AnchorlessComponent>(identity.OriginalEntity, ent),
            OriginalName = identity.OriginalName,
            Starting = identity.Starting,
        }).ToList();
        ent.Comp.CurrentIdentity = EnsureEntity<AnchorlessComponent>(state.CurrentIdentity, ent);
        ent.Comp.IdentityCloningSettings = state.IdentityCloningSettings;
        ent.Comp.HorrorForm = state.HorrorForm;
        ent.Comp.HorrorSprite = state.HorrorSprite;
        ent.Comp.HorrorSpriteState = state.HorrorSpriteState;
        _movement.RefreshMovementSpeedModifiers(ent.Owner, CompOrNull<MovementSpeedModifierComponent>(ent));
        UpdateHorrorVisual(ent);
    }

    private void UpdateHorrorVisual(Entity<AnchorlessComponent> ent)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        if (ent.Comp.HorrorForm)
        {
            if (_sprite.LayerMapTryGet((ent, sprite), HorrorVisualLayer.Key, out _, false))
                return;

            _hiddenLayers[ent.Owner] = sprite.AllLayers.Select(layer => layer.Visible).ToList();
            for (var i = 0; i < _hiddenLayers[ent.Owner].Count; i++)
                _sprite.LayerSetVisible((ent, sprite), i, false);

            var layer = _sprite.AddLayer((ent, sprite),
                new SpriteSpecifier.Rsi(ent.Comp.HorrorSprite, ent.Comp.HorrorSpriteState));

             _sprite.LayerSetScale((ent, sprite), layer, ent.Comp.HorrorScale);

            _sprite.LayerMapSet((ent, sprite), HorrorVisualLayer.Key, layer);
            return;
        }

        if (_sprite.LayerMapTryGet((ent, sprite), HorrorVisualLayer.Key, out var oldLayer, false))
            _sprite.RemoveLayer((ent, sprite), oldLayer);

        if (_hiddenLayers.Remove(ent.Owner, out var visibleLayers))
            for (var i = 0; i < visibleLayers.Count; i++)
                _sprite.LayerSetVisible((ent, sprite), i, visibleLayers[i]);
    }

    private enum HorrorVisualLayer
    {
        Key,
    }
}
