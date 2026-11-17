using System.Numerics;
using Content.Shared._KS14.NPC;
using Robust.Shared.Map;

namespace Content.Server._KS14.NPC.Perception;

/// <summary>
///     One NPC's belief about one hostile.
/// </summary>
/// <param name="FirstSeen">When the current sighting began.</param>
/// <param name="LastSeen">When it was last in sight, or last called out for a <see cref="NpcContactState.Reported"/> one.</param>
/// <param name="LastConspicuous">When it was last easy to see: lit, close, or moving fast. Lets an NPC keep
///     following a target it is already watching into the dark, for a while.</param>
/// <param name="LastKnownCoordinates">Where it was last seen. A snapshot relative to the grid or map, never to the
///     target itself, so it stays put once the target is out of sight.</param>
/// <param name="LastKnownVelocity">Its world velocity when last seen, for guessing where it went.</param>
/// <param name="ContainerUid">The storage it is believed to be in, while <see cref="NpcContactState.Concealed"/>
///     or <see cref="NpcContactState.Suspected"/>.</param>
/// <param name="Reacted">Whether the NPC has reacted to it yet. Sticks while it stays in sight, and for a short while
///     after, so a target being fought does not become a surprise again by stepping out of view for a moment.</param>
/// <param name="ReactAt">When the NPC reacts to it, if it has not yet: the moment it was noticed plus the reaction
///     time. The target does not have to stay in sight until then - a glimpse is enough to be reacted to, the
///     reaction is just late. Default for one that was never seen, only called out.</param>
/// <param name="ObserverWasMoving">Whether the NPC was on the move when it lost sight of it. One that was pushing
///     carries on and investigates; one that was holding stays put and watches for it to show itself again.</param>
/// <param name="RevealedUntil">While this is in the future, the target is known wherever it is, however far or hidden:
///     it shot the NPC. See <see cref="NpcPerceptionComponent.RevealAttackers"/>. Default if it never did.</param>
/// <param name="Traits">What it made of the hostile as it lost sight of it (<see cref="NpcPerceptionSystem.GetTraits"/>).
///     <see cref="NpcContactTraits.None"/> for one never lost from sight, or only called out.</param>
/// <param name="SeenLight">How lit it looked when last seen, after the NPC's dark vision: 1 within
///     <see cref="NpcPerceptionComponent.ProximityRange"/>, NaN if seeing it did not need the light (it was moving fast).
///     Read it through <see cref="NpcPerceptionSystem.GetSeenLight"/>, which works it out when it is missing.</param>
public record struct NpcContact(
    NpcContactState State,
    TimeSpan FirstSeen,
    TimeSpan LastSeen,
    TimeSpan LastConspicuous,
    EntityCoordinates LastKnownCoordinates,
    Vector2 LastKnownVelocity,
    EntityUid? ContainerUid,
    bool Reacted,
    TimeSpan ReactAt = default,
    bool ObserverWasMoving = false,
    TimeSpan RevealedUntil = default,
    NpcContactTraits Traits = NpcContactTraits.None,
    float SeenLight = float.NaN);
