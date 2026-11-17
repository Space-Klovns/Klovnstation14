using Content.Server._KS14.NPC.Hands;

namespace Content.Server._KS14.NPC.Charges;

/// <summary>
///     On an NPC sticking a charge on something. See <see cref="NpcChargeSystem"/>.
/// </summary>
[RegisterComponent, Access(typeof(NpcChargeSystem))]
public sealed partial class NpcPlantingChargeComponent : Component
{
    /// <summary>
    ///     How to put its hands back.
    /// </summary>
    public NpcTakenOut? TakenOut;

    public TimeSpan GiveUpAt;

    /// <summary>
    ///     When it may have a go at sticking it on.
    /// </summary>
    public TimeSpan NextAttemptAt;
}
