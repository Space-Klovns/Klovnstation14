namespace Content.Server._KS14.NPC.Grenades;

/// <summary>
///     Marks a smoke grenade a squad may put into a room before going in. What it does is read from its
///         <see cref="Content.Shared.Trigger.Components.Effects.SmokeOnTriggerComponent"/>: smoke that carries something
///         gasses whatever breathes it, and smoke that blocks sight screens anyone who sees by it, which no NPC does.
/// </summary>
/// <remarks>
///     A marker rather than anything with a smoke trigger, so foam - cleaner, metal foam - is never thrown into a room.
/// </remarks>
[RegisterComponent]
public sealed partial class NpcRoomSmokeComponent : Component;
