using Robust.Shared.Serialization;

namespace Content.Shared.Light
{
    [Serializable, NetSerializable]
    public enum PoweredLightVisuals : byte
    {
        BulbState,
        Blinking,
        Color // KS14 addition: doesnt actually do anything by itself, just forces appearances to update lol
    }

    [Serializable, NetSerializable]
    public enum PoweredLightState : byte
    {
        Empty,
        On,
        Off,
        Broken,
        Burned
    }

    public enum PoweredLightLayers : byte
    {
        Base,
        Glow
    }
}
