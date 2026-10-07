namespace Content.Server._KS14.NPC.Squad.Tactics;

/// <summary>
///     The order a squad's tactics have given this NPC, if any. See <see cref="NpcSquadTacticsSystem"/>.
/// </summary>
[RegisterComponent]
[Access(typeof(NpcSquadTacticsSystem))]
public sealed partial class NpcOrderComponent : Component
{
    [ViewVariables]
    public NpcOrder? Order;
}
