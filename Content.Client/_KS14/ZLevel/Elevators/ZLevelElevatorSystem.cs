using Content.Shared._KS14.ZLevel.Elevators;

namespace Content.Client._KS14.ZLevel.Elevators;

/// <summary>
///     The client half of elevators, which is deliberately nothing.
/// </summary>
/// <remarks>
///     The shared system already recomputes <see cref="ActiveZLevelElevatorComponent.Height"/> from the
///         clock on this side, and everything the ride looks like falls out of that: the viewport reads it
///         in ScalingViewport.GetViewerTransitHeight to draw the levels below at fractional depth, and
///         KsZLevelTransitSpriteSystem reads it to compensate the sprites in those passes against the same
///         number. Nothing else about an elevator is a client decision, so this exists only because the
///         shared system is abstract.
///     One visible limitation of the current movement approach is worth knowing about: a riding viewer's
///         own z-level is drawn through the scaled eye like any other pass, so the elevator's own floor
///         tiles recede along with the level it is leaving rather than staying fixed underfoot. Riders are
///         deliberately not compensated for that - pinning them while the floor under them shrinks looks
///         far worse than the two moving together. Fixing it properly means drawing the elevator in a pass
///         of its own, which is what the transit-map approach behind
///         <see cref="SharedZLevelElevatorSystem.TryCrossToZLevel"/> would buy.
/// </remarks>
public sealed partial class ZLevelElevatorSystem : SharedZLevelElevatorSystem;
