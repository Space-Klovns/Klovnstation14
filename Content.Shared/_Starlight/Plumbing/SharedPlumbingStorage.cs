using Content.Shared.FixedPoint;
using Robust.Shared.Serialization;

namespace Content.Shared._Starlight.Plumbing;

[Serializable, NetSerializable]
public enum PlumbingStorageUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class PlumbingStorageBoundUserInterfaceState : BoundUserInterfaceState
{
    public Dictionary<string, FixedPoint2> Contents { get; }
    public FixedPoint2 Volume { get; }
    public FixedPoint2 MaxVolume { get; }

    public PlumbingStorageBoundUserInterfaceState(Dictionary<string, FixedPoint2> contents, FixedPoint2 volume, FixedPoint2 maxVolume)
    {
        Contents = contents;
        Volume = volume;
        MaxVolume = maxVolume;
    }
}
