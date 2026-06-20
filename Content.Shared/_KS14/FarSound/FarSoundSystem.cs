using System.Numerics;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Collections;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Farsound;

// did you know pvs entities in range of you are pvs overriden and so is their parents or whatever

public sealed class FarSoundSystem : EntitySystem
{
    [Dependency] private readonly INetManager _netManager = default!;
    [Dependency] private readonly ISharedPlayerManager _playerManager = default!;
    [Dependency] private readonly SharedTransformSystem _transformSystem = default!;
    [Dependency] private readonly SharedAudioSystem _audioSystem = default!;

    public Filter GetFilter(EntityUid sourceUid, float minimumRange, float maximumRange)
    {
        var ourMapCoordinates = _transformSystem.GetMapCoordinates(sourceUid);

        // dreams of prediction
        // if (_netManager.IsClient)
        // {
        //     var localFilter = Filter.Empty();
        //     if (_playerManager.LocalEntity is not { } localUid)
        //         return localFilter;

        //     var localMapCoordinates = _transformSystem.GetMapCoordinates(localUid);
        //     if (localMapCoordinates.MapId != ourMapCoordinates.MapId)
        //         return localFilter;

        //     var distance = (ourMapCoordinates.Position - localMapCoordinates.Position).Length();
        //     if (distance < minimumRange ||
        //         distance > maximumRange)
        //         return localFilter;

        //     localFilter.AddPlayer(_playerManager.LocalSession!);
        //     return localFilter;
        // }

        var sessions = _playerManager.NetworkedSessions;
        var filter = Filter.Empty();
        if (sessions.Length == 0)
            return filter;

        var minimumRangeSquared = minimumRange * minimumRange;
        var maximumRangeSquared = maximumRange * maximumRange;
        var eligible = new ValueList<ICommonSession>();

        foreach (var session in sessions)
        {
            if (session.AttachedEntity is not { } attachedUid ||
                TerminatingOrDeleted(attachedUid))
                continue;

            var otherMapCoordinates = _transformSystem.GetMapCoordinates(attachedUid);
            if (otherMapCoordinates.MapId != ourMapCoordinates.MapId)
                continue;

            var distanceSquared = (ourMapCoordinates.Position - otherMapCoordinates.Position).LengthSquared();
            if (distanceSquared < minimumRangeSquared ||
                distanceSquared > maximumRangeSquared)
                continue;

            eligible.Add(session);
        }

        if (eligible.Count == 0)
            return filter;

        return filter.AddPlayers(eligible);
    }

    public static AudioParams GetParams(float minimumRange, float maximumRange, AudioParams? providedAudioParams = null)
    {
        var audioParams = providedAudioParams ?? AudioParams.Default;
        audioParams.ReferenceDistance = minimumRange;
        audioParams.MaxDistance = maximumRange;

        return audioParams;
    }

    public (Filter Filter, AudioParams Params) GetData(EntityUid sourceUid, float minimumRange, float maximumRange, AudioParams? providedAudioParams = null)
        => (GetFilter(sourceUid, minimumRange, maximumRange), GetParams(minimumRange, maximumRange, providedAudioParams: providedAudioParams));

    public void PlayFarSound(EntityUid sourceUid, FarSoundData data, EntityUid? userUid = null)
    {
        // fuck i give up on predicting this
        // this would otherwise require map-coordinates which is unfeasible unless every source of farsounds is magically always in PVS for everybaldi on the map
        if (_netManager.IsClient)
            return;

        var (filter, audioParams) = GetData(sourceUid, data.Range.X, data.Range.Y, data.Sound.Params);
        if (userUid is { })
            filter.RemovePlayerByAttachedEntity(userUid.Value);

        // Don't record in replay or something
        _audioSystem.PlayEntity(data.Sound, filter, sourceUid, false, audioParams: audioParams);
    }

    public bool TryPlayFarSound(EntityUid? sourceUid, FarSoundData? data, EntityUid? userUid = null)
    {
        if (sourceUid is not { } ||
            data is not { })
            return false;

        PlayFarSound(sourceUid.Value, data, userUid: userUid);
        return true;
    }
}

[DataDefinition]
public sealed partial class FarSoundData : ISerializationHooks
{
    [DataField(required: true)]
    public SoundSpecifier Sound = default!;

    [DataField(required: true)]
    public Vector2 Range;

    void ISerializationHooks.AfterDeserialization()
    {
        if (Range.X > Range.Y)
            throw new InvalidOperationException($"Provided range had higher minimum {Range.X} than maximum {Range.Y}");
    }
}
