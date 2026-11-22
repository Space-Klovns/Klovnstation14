using System.Numerics;
using Content.Shared._KS14.Mortar;
using Robust.Server.GameObjects;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Server._KS14.Mortar;

/// <summary>
///     The sight in a mortar's window: for each user with it open, a view-only eye at their aim, which their session is
///         subscribed to (as a surveillance camera's viewer is), so that what is around the aim is sent to them however far
///         off it is. It follows their aim (<see cref="KsMortarAimMessage"/>), no more often than the mortar's
///         <see cref="KsMortarComponent.SightMoveInterval"/>, and goes when they close the window, walk out of its range or
///         the mortar goes.
/// </summary>
public sealed partial class KsMortarSystem
{
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private SharedEyeSystem _eyeSystem = default!;
    [Dependency] private ViewSubscriberSystem _viewSubscriberSystem = default!;
    [Dependency] private EntityQuery<ActorComponent> _actorQuery = default!;
    [Dependency] private EntityQuery<EyeComponent> _eyeQuery = default!;
    [Dependency] private EntityQuery<KsMortarSightViewerComponent> _sightViewerQuery = default!;

    /// <summary>
    ///     Tiles of view past the edge of the sight, so that what is at its edge is there before it comes into it.
    /// </summary>
    private const float SightViewMargin = 4f;

    /// <summary>
    ///     The users of the mortar being updated, taken out to be walked: moving a sight whose eye is gone closes it,
    ///         which takes it out of the mortar's own dictionary.
    /// </summary>
    private readonly List<EntityUid> _sightUsers = new();

    /// <summary>
    ///     Closing the window by any road (the player, the range check, the user going) ends their sight.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnUiClosed(Entity<KsMortarComponent> entity, ref BoundUIClosedEvent args)
    {
        if (!args.UiKey.Equals(KsMortarUiKey.Key) || !CloseSight(entity, args.Actor))
            return;

        if (!TerminatingOrDeleted(entity))
            UpdateUi(entity);
    }

    [SubscribeLocalEvent]
    private void OnAimMessage(Entity<KsMortarComponent> entity, ref KsMortarAimMessage args)
    {
        TrySetSightAim(entity, args.Actor, args.Bearing, args.Distance);
    }

    /// <summary>
    ///     Gives <paramref name="userUid"/> a sight at the middle of the range, due north, until they aim it. Nothing for a
    ///         mortar with no sight, nobody playing, or one they already have.
    /// </summary>
    private void OpenSight(Entity<KsMortarComponent> mortar, EntityUid userUid)
    {
        if (mortar.Comp.SightRadius <= 0f ||
            mortar.Comp.Sights.ContainsKey(userUid) ||
            !_actorQuery.TryComp(userUid, out var actorComponent) ||
            Transform(mortar).MapUid is not { } mapUid)
            return;

        var origin = _transformSystem.GetMapCoordinates(mortar);
        var offset = KsMortarAim.ToOffset(Angle.Zero, (mortar.Comp.MinRange + mortar.Comp.MaxRange) / 2f);
        var eyeUid = Spawn(mortar.Comp.SightEye, new EntityCoordinates(mapUid, origin.Position + offset));

        // Sent only as far around it as the sight shows.
        var viewRange = _configurationManager.GetCVar(CVars.NetMaxUpdateRange);
        if (viewRange > 0f && _eyeQuery.TryComp(eyeUid, out var eyeComponent))
            _eyeSystem.SetPvsScale((eyeUid, eyeComponent), Math.Clamp((mortar.Comp.SightRadius + SightViewMargin) * 2f / viewRange, 0.1f, 1f));

        _viewSubscriberSystem.AddViewSubscriber(eyeUid, actorComponent.PlayerSession);
        mortar.Comp.Sights[userUid] = new KsMortarSight(eyeUid, actorComponent.PlayerSession);

        // Which eye is theirs goes to that player alone, not to everyone with the window open.
        var viewerComponent = EnsureComp<KsMortarSightViewerComponent>(userUid);
        viewerComponent.Eyes[mortar.Owner] = eyeUid;
        Dirty(userUid, viewerComponent);
    }

    /// <summary>
    ///     Ends <paramref name="userUid"/>'s sight: unsubscribes them and deletes its eye. Returns whether they had one.
    /// </summary>
    private bool CloseSight(Entity<KsMortarComponent> mortar, EntityUid userUid)
    {
        if (!mortar.Comp.Sights.Remove(userUid, out var sight))
            return false;

        EndSight(mortar.Owner, userUid, sight);
        return true;
    }

    private void CloseAllSights(Entity<KsMortarComponent> mortar)
    {
        foreach (var (userUid, sight) in mortar.Comp.Sights)
        {
            EndSight(mortar.Owner, userUid, sight);
        }

        mortar.Comp.Sights.Clear();
    }

    private void EndSight(EntityUid mortarUid, EntityUid userUid, KsMortarSight sight)
    {
        if (_sightViewerQuery.TryComp(userUid, out var viewerComponent) && viewerComponent.Eyes.Remove(mortarUid))
        {
            if (viewerComponent.Eyes.Count == 0)
                RemCompDeferred<KsMortarSightViewerComponent>(userUid);
            else
                Dirty(userUid, viewerComponent);
        }

        _viewSubscriberSystem.RemoveViewSubscriber(sight.EyeUid, sight.Session);
        if (!TerminatingOrDeleted(sight.EyeUid))
            QueueDel(sight.EyeUid);
    }

    /// <summary>
    ///     Aims <paramref name="userUid"/>'s sight at <paramref name="bearing"/> and <paramref name="distance"/>, kept to the
    ///         mortar's range: at once, or when its <see cref="KsMortarComponent.SightMoveInterval"/> is up. Refused for what
    ///         is not a number, and for anyone without a sight. Returns whether it was taken.
    /// </summary>
    public bool TrySetSightAim(Entity<KsMortarComponent> mortar, EntityUid userUid, Angle bearing, float distance)
    {
        if (!double.IsFinite(bearing.Theta) ||
            !float.IsFinite(distance) ||
            !mortar.Comp.Sights.TryGetValue(userUid, out var sight))
            return false;

        distance = Math.Clamp(distance, mortar.Comp.MinRange, mortar.Comp.MaxRange);
        sight.PendingOffset = KsMortarAim.ToOffset(bearing, distance);

        var now = _gameTiming.CurTime;
        if (now >= sight.NextMoveAt)
            MoveSight(mortar, userUid, sight, now);

        return true;
    }

    /// <summary>
    ///     Moves every sight whose aim has waited out its interval.
    /// </summary>
    private void UpdateSights(Entity<KsMortarComponent> mortar, TimeSpan now)
    {
        if (mortar.Comp.Sights.Count == 0)
            return;

        _sightUsers.Clear();
        _sightUsers.AddRange(mortar.Comp.Sights.Keys);
        foreach (var userUid in _sightUsers)
        {
            if (mortar.Comp.Sights.TryGetValue(userUid, out var sight) && sight.PendingOffset != null && now >= sight.NextMoveAt)
                MoveSight(mortar, userUid, sight, now);
        }
    }

    private void MoveSight(Entity<KsMortarComponent> mortar, EntityUid userUid, KsMortarSight sight, TimeSpan now)
    {
        if (sight.PendingOffset is not { } offset)
            return;

        sight.PendingOffset = null;
        sight.NextMoveAt = now + mortar.Comp.SightMoveInterval;

        // Its eye deleted by something else: the sight is over, and the window shows none.
        if (TerminatingOrDeleted(sight.EyeUid))
        {
            CloseSight(mortar, userUid);
            UpdateUi(mortar);
            return;
        }

        if (Transform(mortar).MapUid is not { } mapUid)
            return;

        var coordinates = new EntityCoordinates(mapUid, _transformSystem.GetMapCoordinates(mortar).Position + offset);
        if (!_transformSystem.IsValid(coordinates))
            return;

        _transformSystem.SetCoordinates(sight.EyeUid, coordinates);
    }

    /// <summary>
    ///     Where <paramref name="userUid"/>'s sight's eye is, if they have one.
    /// </summary>
    public EntityUid? GetSightEye(KsMortarComponent mortarComponent, EntityUid userUid)
    {
        return mortarComponent.Sights.TryGetValue(userUid, out var sight) ? sight.EyeUid : null;
    }
}
