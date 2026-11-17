using Robust.Shared.Serialization;
using Robust.Shared.Timing;

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

    /// <summary>
    ///     KS14: the server tick it was fired on. A flash can reach a client long after the shot, behind everything else sent
    ///         reliably, when the sound and the bullet came on time with the game state; the client does not show one that
    ///         late. The whole story is at the client's <c>GunSystem.CreateEffect</c>.
    /// </summary>
    public GameTick FiredAt;

    public MuzzleFlashEvent(NetEntity uid, string prototype, string? detachedPrototype /* STDA14 */, Angle angle)
    {
        Uid = uid;
        Prototype = prototype;
        DetachedPrototype = detachedPrototype; // STDA14
        Angle = angle;
    }
}
