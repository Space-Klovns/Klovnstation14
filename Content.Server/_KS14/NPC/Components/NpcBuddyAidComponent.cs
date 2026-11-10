namespace Content.Server._KS14.NPC.Components;

/// <summary>
///     Marks an NPC that uses the meds it carries on a squadmate that is down, once it is safe to reach them. Only an HTN
///         branch reads this.
/// </summary>
[RegisterComponent]
public sealed partial class NpcBuddyAidComponent : Component;
