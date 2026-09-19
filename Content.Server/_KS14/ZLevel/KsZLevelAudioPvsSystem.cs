using System.Numerics;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.ZLevel;
using Robust.Server.GameStates;
using Robust.Shared.Audio.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server._KS14.ZLevel;

/// <summary>
///     Sends players the sounds playing on the z-levels above them, which nothing else would.
/// </summary>
/// <remarks>
///     A sound's own filter is map-gated - Filter.AddInRange requires the listener be on the same map as the
///         sound - and <see cref="KsZLevelPvsSystem"/> only ever mirrors downwards, because downwards is all
///         that is drawn. So a client is never told a sound above it exists, and the client half of the leak
///         has nothing to make audible.
///     This is the narrowest fix for that: a PVS override on the sound entity alone, for the players who could
///         hear it. Note that an override carries an entity's parents with it, so a sound attached to somebody
///         one z-level up also sends them - one entity per sound, and not one that gets drawn, since the
///         renderer only ever walks down the stack.
/// </remarks>
public sealed partial class KsZLevelAudioPvsSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private ISharedPlayerManager _playerManager = default!;
    [Dependency] private KsZLevelSystem _zLevelSystem = default!;
    [Dependency] private PvsOverrideSystem _pvsOverrideSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    [Dependency] private EntityQuery<KsZLevelComponent> _zLevelQuery = default!;
    [Dependency] private EntityQuery<MapComponent> _mapQuery = default!;
    [Dependency] private EntityQuery<TransformComponent> _transformQuery = default!;

    private int _maximumLevels;
    private float _sendRange;

    /// <summary>
    ///     Who is listening from under each map, rebuilt every tick.
    /// </summary>
    /// <remarks>
    ///     Keyed by the map a sound would have to be on to be worth sending, so that the pass over the sounds
    ///         themselves - much the larger of the two - is a single lookup each.
    /// </remarks>
    private readonly Dictionary<MapId, List<(ICommonSession Session, Vector2 Position)>> _listenersUnder = [];

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_configurationManager, KsCCVars.ZLevelAudioLeakLevels, value => _maximumLevels = value, true);
        Subs.CVar(_configurationManager, KsCCVars.ZLevelAudioLeakSendRange, value => _sendRange = value, true);
    }

    /// <summary>
    ///     Every tick rather than on a timer: a sound is only a second or two long, so anything slower would
    ///         reliably clip the start off one.
    /// </summary>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_maximumLevels <= 0)
            return;

        GatherListeners();

        if (_listenersUnder.Count == 0)
            return;

        var query = AllEntityQuery<AudioComponent, TransformComponent>();
        while (query.MoveNext(out var audioUid, out _, out var transformComponent))
        {
            if (transformComponent.MapID == MapId.Nullspace ||
                !_listenersUnder.TryGetValue(transformComponent.MapID, out var listeners))
            {
                continue;
            }

            var worldPosition = _transformSystem.GetWorldPosition(transformComponent);

            foreach (var (session, listenerPosition) in listeners)
            {
                if ((listenerPosition - worldPosition).Length() > _sendRange)
                    continue;

                // A set add, so repeating it every tick for the life of the sound costs nothing. The engine
                //      drops the entry itself when the sound entity is deleted.
                _pvsOverrideSystem.AddSessionOverride(audioUid, session);
            }
        }
    }

    private void GatherListeners()
    {
        _listenersUnder.Clear();

        foreach (var session in _playerManager.NetworkedSessions)
        {
            if (session.AttachedEntity is not { } attachedUid ||
                !_transformQuery.TryGetComponent(attachedUid, out var transformComponent) ||
                transformComponent.MapUid is not { } mapUid ||
                !_zLevelQuery.HasComponent(mapUid))
            {
                continue;
            }

            var listenerPosition = _transformSystem.GetWorldPosition(transformComponent);

            var above = mapUid;

            for (var level = 0; level < _maximumLevels; level++)
            {
                if (!_zLevelSystem.TryGetZLevelAbove(above, out var aboveEntity))
                    break;

                above = aboveEntity.Value.Owner;

                if (!_mapQuery.TryGetComponent(above, out var mapComponent))
                    break;

                _listenersUnder.GetOrNew(mapComponent.MapId).Add((session, listenerPosition));
            }
        }
    }
}
