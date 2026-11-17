namespace Content.Server._KS14.NPC.Dangers;

/// <summary>
///     Makes the whole tile this entity is on dangerous to walk onto, for NPCs that avoid that sort of danger. See
///         <see cref="NpcAvoidsTileDangerComponent"/>.
/// </summary>
[RegisterComponent]
public sealed partial class NpcTileDangerComponent : Component
{
    [DataField]
    public NpcTileDangerKind Dangers = NpcTileDangerKind.Fire;
}
