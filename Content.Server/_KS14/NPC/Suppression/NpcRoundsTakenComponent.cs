namespace Content.Server._KS14.NPC.Suppression;

/// <summary>
///     On something NPCs have fired at to break it (a locker, a window): how many rounds it has taken from them so far, all
///         NPCs together. Something that has taken a magazine and still stands is not worth another: see
///         <see cref="NpcSuppressiveFireSystem.GetRoundsTaken"/>. Goes with what it is on.
/// </summary>
[RegisterComponent]
[Access(typeof(NpcSuppressiveFireSystem))]
public sealed partial class NpcRoundsTakenComponent : Component
{
    [ViewVariables]
    public int Rounds;
}
