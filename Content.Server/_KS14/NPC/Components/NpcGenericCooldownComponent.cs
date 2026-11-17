using Content.Server._KS14.NPC.Systems;

namespace Content.Server._KS14.NPC.Components;

/// <summary>
///     An NPC's named cooldowns. See <see cref="NpcGenericCooldownSystem"/>.
/// </summary>
[RegisterComponent, Access(typeof(NpcGenericCooldownSystem))]
public sealed partial class NpcGenericCooldownComponent : Component
{
    /// <summary>
    ///     When each cooldown ends, by the hash of its key. Hashes of strings differ from one run of the server to the
    ///         next, so this is never saved.
    /// </summary>
    [ViewVariables]
    public Dictionary<int, TimeSpan> CooldownEndTimes = new();
}
