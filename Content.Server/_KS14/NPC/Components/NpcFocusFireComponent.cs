namespace Content.Server._KS14.NPC.Components;

/// <summary>
///     Marks an NPC that prefers to shoot what a squadmate is already shooting. Only its target query reads this.
/// </summary>
[RegisterComponent]
public sealed partial class NpcFocusFireComponent : Component;
