using Content.Shared.NPC.Prototypes;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Perks.Support;

/// <summary>
///     An ammo resupply on the floor. A living friendly of whoever dropped it (sharing an NPC faction with them) who
///         comes within <see cref="Radius"/> has the gun in their active hand refilled, with its magazine's own
///         rounds, or its battery recharged, and the pickup is used up. With nothing to refill it stays. Despawning is
///         left to <c>TimedDespawn</c> on the prototype. Taken in shared code (<see cref="KsAmmoPickupSystem"/>): by the
///         server for everyone, and predicted by a player's own client for that player.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(KsAmmoPickupSystem))]
public sealed partial class KsAmmoPickupComponent : Component
{
    [DataField, AutoNetworkedField]
    public float Radius = 0.6f;

    /// <summary>
    ///     How often it looks for someone to take it. Both sides look on the same ticks, counted from the tick number,
    ///         so a client predicting its player's take does it on the tick the server does.
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
    /// <seealso cref="KsAmmoPickupSystem.SetFactions"/>
    [DataField, AutoNetworkedField]
    public HashSet<ProtoId<NpcFactionPrototype>> Factions = new();
}
