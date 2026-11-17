using Robust.Client.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._KS14.PredictedSpawning;

/// <summary>
///     Keeps the placeholders this client lays on tiles for its own predicted actions until the server's entities
///         replace them. See <see cref="KsTilePlaceholderComponent"/>. Each feature decides when the server's entity for
///         a tile has arrived, and calls <see cref="Supersede"/>.
/// </summary>
public sealed partial class KsTilePlaceholderSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private SpriteSystem _spriteSystem = default!;

    [Dependency] private EntityQuery<KsTilePlaceholderComponent> _placeholderQuery = default!;
    [Dependency] private EntityQuery<SpriteComponent> _spriteQuery = default!;

    /// <summary>
    ///     Spawns <paramref name="prototypeId"/> as a placeholder: a plain client-side entity rather than a predicted
    ///         spawn, so the next state leaves it alone, removed after <paramref name="lifetime"/> unless superseded.
    /// </summary>
    public EntityUid Spawn(EntProtoId prototypeId, EntityCoordinates coordinates, TimeSpan lifetime)
    {
        var uid = SpawnAttachedTo(prototypeId, coordinates);
        AddComp(uid, new KsTilePlaceholderComponent { ExpiryTime = _gameTiming.CurTime + lifetime });

        return uid;
    }

    /// <summary>
    ///     Whether <paramref name="uid"/> is a placeholder, superseded or not.
    /// </summary>
    public bool IsPlaceholder(EntityUid uid) => _placeholderQuery.HasComp(uid);

    /// <summary>
    ///     Whether <paramref name="uid"/> is a placeholder the server's entity has not replaced yet.
    /// </summary>
    public bool IsWaiting(EntityUid uid) => _placeholderQuery.TryComp(uid, out var placeholderComponent) && !placeholderComponent.Superseded;

    /// <summary>
    ///     The placeholder now also waits for a later action's server entity, so its expiry restarts.
    /// </summary>
    public void Refresh(EntityUid uid, TimeSpan lifetime)
    {
        if (!_placeholderQuery.TryComp(uid, out var placeholderComponent) || placeholderComponent.Superseded)
            return;

        placeholderComponent.ExpiryTime = _gameTiming.CurTime + lifetime;
    }

    /// <summary>
    ///     The server's entity has taken the placeholder's tile over. Hidden at once, so the tile never shows two;
    ///         deleted in <see cref="FrameUpdate"/>, because this usually runs while a state is applied, and deleting a
    ///         client-side entity destroys its contacts there and then.
    /// </summary>
    public void Supersede(EntityUid uid)
    {
        if (!_placeholderQuery.TryComp(uid, out var placeholderComponent))
            return;

        placeholderComponent.Superseded = true;

        if (_spriteQuery.TryComp(uid, out var spriteComponent))
            _spriteSystem.SetVisible((uid, spriteComponent), false);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var curTime = _gameTiming.CurTime;

        // Superseded, or never superseded in time. Out here, clear of state application and of physics.
        var enumerator = EntityQueryEnumerator<KsTilePlaceholderComponent>();
        while (enumerator.MoveNext(out var uid, out var placeholderComponent))
        {
            if (placeholderComponent.Superseded || curTime >= placeholderComponent.ExpiryTime)
                QueueDel(uid);
        }
    }
}
