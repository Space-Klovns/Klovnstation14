using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Cultivation;

// Techniques every cultivator learns as they rise, the rest of each law's techniques, and the combinations two
// elements open together. Each event carries its own numbers so the action prototypes can tune them.

#region Universal

public sealed partial class SpiritualSenseActionEvent : InstantActionEvent, Content.Shared._KS14.Cultivation.IQiCostEvent
{
    [DataField]
    public float QiCost { get; set; } = 5f;

    [DataField]
    public float Range = 10f;
}

public sealed partial class EmptyPalmActionEvent : EntityTargetActionEvent, Content.Shared._KS14.Cultivation.IQiCostEvent
{
    [DataField]
    public float QiCost { get; set; } = 10f;

    [DataField]
    public float Distance = 4f;

    [DataField]
    public float StaminaDamage = 15f;
}

public sealed partial class QinggongActionEvent : WorldTargetActionEvent, Content.Shared._KS14.Cultivation.IQiCostEvent
{
    [DataField]
    public float QiCost { get; set; } = 10f;

    /// <summary>
    ///     Tiles at Qi Condensation; two more for every realm above it.
    /// </summary>
    [DataField]
    public float BaseDistance = 4f;
}

public sealed partial class VoidStepActionEvent : WorldTargetActionEvent, Content.Shared._KS14.Cultivation.IQiCostEvent
{
    [DataField]
    public float QiCost { get; set; } = 25f;

    [DataField]
    public float MaxDistance = 6f;
}

public sealed partial class RealmPressureActionEvent : InstantActionEvent, Content.Shared._KS14.Cultivation.IQiCostEvent
{
    [DataField]
    public float QiCost { get; set; } = 40f;

    [DataField]
    public float Range = 5f;

    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(6);

    [DataField]
    public float StaminaDamage = 30f;
}

public sealed partial class AcupointSealingActionEvent : EntityTargetActionEvent, Content.Shared._KS14.Cultivation.IQiCostEvent
{
    [DataField]
    public float QiCost { get; set; } = 25f;

    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(10);
}

public sealed partial class WriteTalismanActionEvent : InstantActionEvent, Content.Shared._KS14.Cultivation.IQiCostEvent
{
    [DataField]
    public float QiCost { get; set; } = 20f;
}

public sealed partial class TransmitLawActionEvent : InstantActionEvent, Content.Shared._KS14.Cultivation.IQiCostEvent
{
    [DataField]
    public float QiCost { get; set; } = 30f;
}

public sealed partial class ConcealmentActionEvent : InstantActionEvent, Content.Shared._KS14.Cultivation.IQiCostEvent
{
    [DataField]
    public float QiCost { get; set; } = 15f;
}

public sealed partial class ForcedCirculationActionEvent : InstantActionEvent;

public sealed partial class ForgetLawActionEvent : InstantActionEvent;

public enum CultivationFormation : byte
{
    /// <summary>
    ///     Draws heaven and earth's qi in: meditating inside is far more effective.
    /// </summary>
    Gathering,

    /// <summary>
    ///     A ring of qi walls only the maker and their friends can pass.
    /// </summary>
    Barrier,

    /// <summary>
    ///     Whispers to the maker whenever a stranger steps inside.
    /// </summary>
    Alarm,
}

public sealed partial class InscribeFormationActionEvent : InstantActionEvent, Content.Shared._KS14.Cultivation.IQiCostEvent
{
    [DataField(required: true)]
    public CultivationFormation Formation;

    [DataField]
    public float QiCost { get; set; } = 30f;
}

public sealed partial class BodyOutsideBodyActionEvent : InstantActionEvent, Content.Shared._KS14.Cultivation.IQiCostEvent
{
    [DataField]
    public float QiCost { get; set; } = 50f;

    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(30);
}

public sealed partial class ReturnToBodyActionEvent : InstantActionEvent;

#endregion

#region Metal: Returning Iron

public sealed partial class BindArtifactActionEvent : InstantActionEvent, Content.Shared._KS14.Cultivation.IQiCostEvent
{
    [DataField]
    public float QiCost { get; set; } = 5f;

    /// <summary>
    ///     How far a bound artifact can be called back from at Qi Condensation; three more per realm.
    /// </summary>
    [DataField]
    public float BaseRecallRange = 7f;
}

public sealed partial class ArtifactStrikeActionEvent : EntityTargetActionEvent, Content.Shared._KS14.Cultivation.IQiCostEvent
{
    [DataField]
    public float QiCost { get; set; } = 12f;

    [DataField]
    public float BonusDamage = 10f;
}

public sealed partial class SwordRidingActionEvent : InstantActionEvent, Content.Shared._KS14.Cultivation.IQiCostEvent
{
    [DataField]
    public float QiCost { get; set; } = 40f;

    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(8);
}

public sealed partial class SwordFormationActionEvent : InstantActionEvent, Content.Shared._KS14.Cultivation.IQiCostEvent
{
    [DataField]
    public float QiCost { get; set; } = 60f;

    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(12);
}

#endregion

#region Water: Still Lake

public sealed partial class CalmHeartActionEvent : EntityTargetActionEvent, Content.Shared._KS14.Cultivation.IQiCostEvent
{
    [DataField]
    public float QiCost { get; set; } = 20f;
}

public sealed partial class TurtleBreathingActionEvent : InstantActionEvent, Content.Shared._KS14.Cultivation.IQiCostEvent
{
    [DataField]
    public float QiCost { get; set; } = 30f;

    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(30);
}

public sealed partial class MirrorLakeActionEvent : InstantActionEvent, Content.Shared._KS14.Cultivation.IQiCostEvent
{
    [DataField]
    public float QiCost { get; set; } = 50f;

    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(8);
}

#endregion

#region Fire: Scorching Sun

public sealed partial class KindleActionEvent : EntityTargetActionEvent, Content.Shared._KS14.Cultivation.IQiCostEvent
{
    [DataField]
    public float QiCost { get; set; } = 10f;
}

public sealed partial class FurnaceBurstActionEvent : InstantActionEvent, Content.Shared._KS14.Cultivation.IQiCostEvent
{
    [DataField]
    public float QiCost { get; set; } = 35f;

    [DataField]
    public float Range = 2.5f;
}

public sealed partial class BurningBloodActionEvent : InstantActionEvent
{
    [DataField]
    public float QiGain = 50f;

    [DataField]
    public float SelfDamage = 15f;

    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(10);
}

#endregion

#region Earth: Immovable Mountain

public sealed partial class GoldenBellQiActionEvent : InstantActionEvent, Content.Shared._KS14.Cultivation.IQiCostEvent
{
    [DataField]
    public float QiCost { get; set; } = 30f;

    /// <summary>
    ///     Seconds of untouchability at Qi Condensation; one more per realm.
    /// </summary>
    [DataField]
    public float BaseSeconds = 3f;
}

public sealed partial class DharmaIdolActionEvent : InstantActionEvent, Content.Shared._KS14.Cultivation.IQiCostEvent
{
    [DataField]
    public float QiCost { get; set; } = 60f;

    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(15);
}

public sealed partial class BuddhaPalmActionEvent : WorldTargetActionEvent, Content.Shared._KS14.Cultivation.IQiCostEvent
{
    [DataField]
    public float QiCost { get; set; } = 60f;

    /// <summary>
    ///     Blunt damage to everyone under the palm, before realm power. Anyone up to two tiles beyond its edge takes a third.
    /// </summary>
    [DataField]
    public float Damage = 60f;
}

#endregion

#region Wood: Evergreen Spring

public sealed partial class VerdantGrowthActionEvent : InstantActionEvent, Content.Shared._KS14.Cultivation.IQiCostEvent
{
    [DataField]
    public float QiCost { get; set; } = 20f;

    [DataField]
    public float Range = 4f;
}

public sealed partial class BindingVinesActionEvent : EntityTargetActionEvent, Content.Shared._KS14.Cultivation.IQiCostEvent
{
    [DataField]
    public float QiCost { get; set; } = 30f;

    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(4);
}

#endregion

#region Combinations

public sealed partial class SteamVeilActionEvent : InstantActionEvent, Content.Shared._KS14.Cultivation.IQiCostEvent
{
    [DataField]
    public float QiCost { get; set; } = 25f;

    [DataField]
    public EntProtoId Smoke = "KsSteamVeil";
}

public sealed partial class ThousandThornsActionEvent : WorldTargetActionEvent, Content.Shared._KS14.Cultivation.IQiCostEvent
{
    [DataField]
    public float QiCost { get; set; } = 30f;

    [DataField]
    public EntProtoId Projectile = "ProjectileCultivationThornSword";

    [DataField]
    public int Count = 5;

    [DataField]
    public float Spread = 50f;
}

public sealed partial class MoltenStepActionEvent : InstantActionEvent, Content.Shared._KS14.Cultivation.IQiCostEvent
{
    [DataField]
    public float QiCost { get; set; } = 25f;

    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(8);
}

public sealed partial class MudPrisonActionEvent : WorldTargetActionEvent, Content.Shared._KS14.Cultivation.IQiCostEvent
{
    [DataField]
    public float QiCost { get; set; } = 25f;

    [DataField]
    public EntProtoId Mud = "KsSuckingMud";
}

#endregion

[Serializable, NetSerializable]
public sealed partial class ForgetLawDoAfterEvent : SimpleDoAfterEvent;

/// <summary>
///     Bind a station animal as a spirit beast: it follows its master and fights whoever attacks them.
/// </summary>
public sealed partial class BeastContractActionEvent : EntityTargetActionEvent, Content.Shared._KS14.Cultivation.IQiCostEvent
{
    [DataField]
    public float QiCost { get; set; } = 30f;
}

/// <summary>
///     Call the contracted spirit beast to your side.
/// </summary>
public sealed partial class SummonBeastActionEvent : InstantActionEvent, Content.Shared._KS14.Cultivation.IQiCostEvent
{
    [DataField]
    public float QiCost { get; set; } = 10f;

    [DataField]
    public float Range = 30f;
}
