namespace Content.Server._KS14.NPC.Squad.Tactics;

/// <summary>
///     A hunt in progress for a hostile that was lost. On the squad's entity, or on an NPC that has no squad and so
///         hunts alone. See <see cref="NpcSquadTacticsSystem"/>.
/// </summary>
[RegisterComponent]
[Access(typeof(NpcSquadTacticsSystem))]
public sealed partial class NpcHuntComponent : Component
{
    [ViewVariables]
    public NpcHunt? Hunt;
}
