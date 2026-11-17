namespace Content.Server._KS14.NPC.Perception;

/// <summary>
///     Marks an occluder that <see cref="NpcLineOfSightSystem"/> cannot find by walking the tiles a ray crosses: one not
///         anchored, or anchored but reaching past its own tile. Line of sight tests each of these on its own. Kept up to
///         date by <see cref="NpcLineOfSightSystem"/> as occluders are spawned, anchored, unanchored and turned.
/// </summary>
[RegisterComponent]
[Access(typeof(NpcLineOfSightSystem))]
public sealed partial class NpcIrregularOccluderComponent : Component;
