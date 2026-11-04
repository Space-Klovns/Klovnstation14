namespace Content.Server._KS14.NPC.Suppression;

/// <summary>
///     Marks an NPC that keeps firing at where a hostile just ducked out of sight, to keep its head down. See
///         <see cref="NpcSuppressiveFireSystem"/>.
/// </summary>
[RegisterComponent]
public sealed partial class NpcSuppressorComponent : Component;
