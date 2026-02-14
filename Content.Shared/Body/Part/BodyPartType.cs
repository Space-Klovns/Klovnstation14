using Content.Shared.Body.Components;
using Robust.Shared.Serialization;

namespace Content.Shared.Body.Part
{
    /// <summary>
    ///     Defines the type of a <see cref="BodyComponent"/>.
    /// </summary>
    [Flags] // KS14 Klovnmed: added FlagsAttribute
    [Serializable, NetSerializable]
    public enum BodyPartType
    {
        Other = 0,
        Torso,
        Head,
        Arm,
        Hand,
        Leg,
        Foot,
        Tail
    }
}
