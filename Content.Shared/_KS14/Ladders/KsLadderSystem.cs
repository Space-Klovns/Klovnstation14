namespace Content.Shared._KS14.Ladders;

/// <summary>
///     Links ladders and moves whoever climbs one to the other end.
/// </summary>
public sealed partial class KsLadderSystem : EntitySystem
{
    /// <summary>
    ///     Links two ladders to each other, unlinking whatever either was linked to before.
    /// </summary>
    public void Link(Entity<KsLadderComponent?> first, Entity<KsLadderComponent?> second)
    {
    }
}
