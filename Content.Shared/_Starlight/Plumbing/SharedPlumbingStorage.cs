using Content.Shared.FixedPoint;
using Robust.Shared.Serialization;

namespace Content.Shared._Starlight.Plumbing;

/// <summary>
///     Key for the plumbing storage UI.
///     
///     CRITICAL DESIGN NOTE:
///     1. Raw Value: Explicitly set to 100 to avoid underlying byte collision with vanilla StorageUiKey.Key (0).
///     2. Naming: The BUI class is named 'PlumbingStorageBui' rather than 'PlumbingStorageBoundUserInterface' 
///        to avoid Robust's LooseGetType reflection collision. Robust uses suffix matching, so 
///        'PlumbingStorageBoundUserInterface' would be incorrectly returned when 'StorageBoundUserInterface' 
///        is requested, causing an InvalidCastException in vanilla systems.
/// </summary>
[Serializable, NetSerializable]
public enum PlumbingStorageUiKey : byte
{
    Key = 100,
}

[Serializable, NetSerializable]
public sealed class PlumbingStorageBuiState : BoundUserInterfaceState
{
    public Dictionary<string, FixedPoint2> Contents { get; }
    public FixedPoint2 Volume { get; }
    public FixedPoint2 MaxVolume { get; }

    public PlumbingStorageBuiState(Dictionary<string, FixedPoint2> contents, FixedPoint2 volume, FixedPoint2 maxVolume)
    {
        Contents = contents;
        Volume = volume;
        MaxVolume = maxVolume;
    }
}
