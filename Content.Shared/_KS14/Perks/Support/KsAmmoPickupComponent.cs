using Content.Shared.NPC.Prototypes;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Perks.Support;

/// <summary>
///     An ammo resupply on the floor. A living friendly of whoever dropped it (sharing an NPC faction with them) who
///         comes within <see cref="Radius"/> has the gun in their active hand refilled, with its magazine's own
///         rounds, or its battery recharged, and the pickup is used up. With nothing to refill it stays. Despawning is
///         left to <c>TimedDespawn</c> on the prototype. Checked on the server (<c>KsAmmoDropSystem</c>).
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class KsAmmoPickupComponent : Component
{
    [DataField]
    public float Radius = 0.6f;

    /// <summary>
    ///     How often it looks for someone to take it.
    /// </summary>
    [DataField]
    public TimeSpan CheckInterval = TimeSpan.FromSeconds(0.2);

    [DataField]
    public SoundSpecifier? PickupSound = new SoundPathSpecifier("/Audio/Weapons/Guns/MagIn/batrifle_magin.ogg");

    [DataField]
    public LocId PickupPopup = "ks-perk-support-ammo-refilled";

    /// <summary>
    ///     The sides that may take it: the dropper's NPC factions when it was dropped.
    /// </summary>
    [DataField]
    public HashSet<ProtoId<NpcFactionPrototype>> Factions = new();

    [DataField, AutoPausedField]
    public TimeSpan NextCheck;
}
