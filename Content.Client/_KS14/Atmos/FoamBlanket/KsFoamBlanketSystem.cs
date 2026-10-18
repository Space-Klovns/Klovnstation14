using Content.Shared._KS14.Atmos.FoamBlanket;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Client.DisplacementMap;
using Robust.Client.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._KS14.Atmos.FoamBlanket;

/// <summary>
///     Lets the client predict the blanket: it keeps the blanket cache, answers whether something burns from the
///         appearance data it is sent, fades the blanket with its foam, and keeps the placeholders its player's own
///         shots lay until the server's blankets replace them (<see cref="KsFoamBlanketPlaceholderComponent"/>).
/// </summary>
public sealed partial class KsFoamBlanketSystem : SharedKsFoamBlanketSystem
{
    /// <summary>
    ///     Opacity of a nearly spent blanket.
    /// </summary>
    private const float MinimumAlpha = 0.35f;

    /// <summary>
    ///     Opacity of a full blanket.
    /// </summary>
    private const float MaximumAlpha = 0.85f;

    [Dependency] private SharedAppearanceSystem _appearanceSystem = default!;
    [Dependency] private SpriteSystem _spriteSystem = default!;
    [Dependency] private DisplacementMapSystem _displacementMapSystem = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private EntityQuery<KsFoamBlanketDissolveComponent> _dissolveQuery = default!;
    [Dependency] private EntityQuery<KsFoamBlanketPlaceholderComponent> _placeholderQuery = default!;
    [Dependency] private EntityQuery<SpriteComponent> _spriteQuery = default!;

    /// <summary>
    ///     The sprite layer the dissolve mask is applied to.
    /// </summary>
    private const string FoamLayerKey = "foam";

    protected override bool IsBurning(Entity<FlammableComponent> flammable)
        => _appearanceSystem.TryGetData<bool>(flammable.Owner, FireVisuals.OnFire, out var onFire) && onFire;

    /// <summary>
    ///     A plain client-side spawn rather than a predicted one, so the next state reset leaves it alone; see
    ///         <see cref="KsFoamBlanketPlaceholderComponent"/>.
    /// </summary>
    protected override EntityUid SpawnPlaceholderBlanket(EntProtoId prototypeId, EntityCoordinates coordinates)
    {
        var blanketUid = SpawnAttachedTo(prototypeId, coordinates);

        var lifetime = TryComp<KsFoamBlanketComponent>(blanketUid, out var blanketComponent)
            ? blanketComponent.PlaceholderLifetime
            : TimeSpan.Zero;
        AddComp(blanketUid, new KsFoamBlanketPlaceholderComponent { ExpiryTime = _gameTiming.CurTime + lifetime });

        return blanketUid;
    }

    protected override void OnPlaceholderCrossed(Entity<KsFoamBlanketComponent> blanket)
    {
        base.OnPlaceholderCrossed(blanket);

        if (!_placeholderQuery.TryComp(blanket.Owner, out var placeholderComponent) || placeholderComponent.Superseded)
            return;

        placeholderComponent.ExpiryTime = _gameTiming.CurTime + blanket.Comp.PlaceholderLifetime;
    }

    /// <summary>
    ///     The server's blanket has arrived on a tile, and taken it over from this client's placeholder. Hidden at
    ///         once, so the tile never shows two; deleted in <see cref="FrameUpdate"/>, because this runs while the
    ///         state is applied, and deleting a client-side entity destroys its contacts there and then.
    /// </summary>
    protected override void OnBlanketSuperseded(EntityUid previousUid)
    {
        base.OnBlanketSuperseded(previousUid);

        if (!_placeholderQuery.TryComp(previousUid, out var placeholderComponent))
            return;

        placeholderComponent.Superseded = true;
        if (_spriteQuery.TryComp(previousUid, out var spriteComponent))
            _spriteSystem.SetVisible((previousUid, spriteComponent), false);
    }

    [SubscribeLocalEvent]
    private void OnAfterAutoHandleState(Entity<KsFoamBlanketComponent> entity, ref AfterAutoHandleStateEvent args)
        => OnBlanketUpdated(entity);

    protected override void OnBlanketUpdated(Entity<KsFoamBlanketComponent> entity)
    {
        UpdateAlpha(entity);

        if (entity.Comp.DissolveEndTime == null)
            return;

        // Only the server starts a dissolve, so the client drops the blanket from its cache itself.
        Unregister(entity);
        ApplyDissolve(entity);
    }

    /// <summary>
    ///     Lays the dissolve mask over the foam layer, once. The mask layer is new, so its animation starts from its
    ///         first frame.
    /// </summary>
    private void ApplyDissolve(Entity<KsFoamBlanketComponent> entity)
    {
        // The end is the client's own: the server's DissolveEndTime is on the server's clock, and the client's
        //     predicted CurTime runs ahead of it, so comparing them hides the blanket early. Kept if already set,
        //     since this runs again with every state.
        if (!_dissolveQuery.HasComp(entity.Owner))
            AddComp(entity.Owner, new KsFoamBlanketDissolveComponent { EndTime = _gameTiming.CurTime + entity.Comp.DissolveDuration });

        if (entity.Comp.DissolveDisplacement is not { } displacement ||
            !TryComp<SpriteComponent>(entity, out var spriteComponent))
            return;

        var sprite = (entity.Owner, spriteComponent);
        if (!_spriteSystem.LayerMapTryGet(sprite, FoamLayerKey, out var foamIndex, logMissing: false) ||
            _spriteSystem.LayerMapTryGet(sprite, FoamLayerKey + "-displacement", out _, logMissing: false))
            return;

        // At the foam layer's own index, so the mask layer lands before it and is copied in the same frame.
        _displacementMapSystem.TryAddDisplacement(displacement, sprite, foamIndex, FoamLayerKey, out _);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var curTime = _gameTiming.CurTime;

        // Superseded, or never superseded in time. Out here, clear of state application and of physics.
        var placeholderEnumerator = EntityQueryEnumerator<KsFoamBlanketPlaceholderComponent>();
        while (placeholderEnumerator.MoveNext(out var placeholderUid, out var placeholderComponent))
        {
            if (placeholderComponent.Superseded || curTime >= placeholderComponent.ExpiryTime)
                QueueDel(placeholderUid);
        }

        var enumerator = EntityQueryEnumerator<KsFoamBlanketDissolveComponent, SpriteComponent>();
        while (enumerator.MoveNext(out var uid, out var dissolveComponent, out var spriteComponent))
        {
            if (curTime < dissolveComponent.EndTime)
                continue;

            _spriteSystem.SetVisible((uid, spriteComponent), false);
            // Hidden for good: the server's removal is on its way.
            RemCompDeferred<KsFoamBlanketDissolveComponent>(uid);
        }
    }

    private void UpdateAlpha(Entity<KsFoamBlanketComponent> entity)
    {
        if (!TryComp<SpriteComponent>(entity, out var spriteComponent))
            return;

        var fullness = entity.Comp.MaxFoam > 0f ? Math.Clamp(entity.Comp.Foam / entity.Comp.MaxFoam, 0f, 1f) : 1f;
        var alpha = MinimumAlpha + (MaximumAlpha - MinimumAlpha) * fullness;

        _spriteSystem.SetColor((entity.Owner, spriteComponent), spriteComponent.Color.WithAlpha(alpha));
    }
}
