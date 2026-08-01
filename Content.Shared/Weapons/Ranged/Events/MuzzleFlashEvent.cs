using Robust.Shared.Serialization;

namespace Content.Shared.Weapons.Ranged.Events;

/// <summary>
/// Raised whenever a muzzle flash client-side entity needs to be spawned.
/// </summary>
[Serializable, NetSerializable]
public sealed class MuzzleFlashEvent : EntityEventArgs
{
    public NetEntity Uid;
    public string Prototype;
    public string? DetachedPrototype; // STDA14

    public Angle Angle;

    public MuzzleFlashEvent(NetEntity uid, string prototype, string? detachedPrototype /* STDA14 */, Angle angle)
    {
        Uid = uid;
        Prototype = prototype;
        DetachedPrototype = detachedPrototype; // STDA14
        Angle = angle;
    }
}
