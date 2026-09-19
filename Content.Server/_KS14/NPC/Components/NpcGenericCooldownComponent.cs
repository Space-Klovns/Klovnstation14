using Content.Server._KS14.NPC.Systems;

namespace Content.Server._KS14.NPC.Components;

[RegisterComponent]
public sealed partial class NpcGenericCooldownComponent : Component
{
    /// <summary>
    ///     Cooldowns sorted by hashcode of their string key, and when they will end.
    /// </summary>
    [DataField]
    [Access(typeof(NpcGenericCooldownSystem))]
    public Dictionary<int, TimeSpan> CooldownEndTimes = [];
}
