using Content.Shared.Actions;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Cultivation;

// Every technique carries its own numbers, so each action prototype can tune them.

#region Metal

/// <summary>
///     Hurl a blade of sword qi. Metal Root: three blades fan out instead of one.
/// </summary>
public sealed partial class CultivationFlyingSwordActionEvent : WorldTargetActionEvent, Content.Shared._KS14.Cultivation.IQiCostEvent
{
    [DataField]
    public float QiCost { get; set; } = 15f;

    [DataField]
    public EntProtoId Projectile = "ProjectileCultivationSwordQi";

    [DataField]
    public float Speed = 20f;

    /// <summary>
    ///     Degrees between blades when a Metal Root fans them out.
    /// </summary>
    [DataField]
    public float RootSpread = 12f;
}

/// <summary>
///     A rain of sword qi: several blades in a cone towards the target.
/// </summary>
public sealed partial class CultivationSwordRainActionEvent : WorldTargetActionEvent, Content.Shared._KS14.Cultivation.IQiCostEvent
{
    [DataField]
    public float QiCost { get; set; } = 40f;

    [DataField]
    public EntProtoId Projectile = "ProjectileCultivationSwordQi";

    [DataField]
    public int Count = 5;

    [DataField]
    public float Spread = 40f;

    [DataField]
    public float Speed = 20f;
}

#endregion

#region Fire

/// <summary>
///     A palm strike wreathed in flame. Fire Root: the flames catch twice as hard.
/// </summary>
public sealed partial class CultivationBurningPalmActionEvent : EntityTargetActionEvent, Content.Shared._KS14.Cultivation.IQiCostEvent
{
    [DataField]
    public float QiCost { get; set; } = 15f;

    [DataField]
    public float HeatDamage = 10f;

    [DataField]
    public float FireStacks = 2f;
}

/// <summary>
///     Set everyone who isn't a friend ablaze around you.
/// </summary>
public sealed partial class CultivationSeaOfFlamesActionEvent : InstantActionEvent, Content.Shared._KS14.Cultivation.IQiCostEvent
{
    [DataField]
    public float QiCost { get; set; } = 45f;

    [DataField]
    public float Range = 3f;

    [DataField]
    public float FireStacks = 3f;
}

#endregion

#region Water

/// <summary>
///     Still water around the body softens every blow. Water Root: the stored force crashes back out when it ends.
/// </summary>
public sealed partial class CultivationStillWaterWardActionEvent : InstantActionEvent, Content.Shared._KS14.Cultivation.IQiCostEvent
{
    [DataField]
    public float QiCost { get; set; } = 20f;

    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(10);

    [DataField]
    public float DamageMultiplier = 0.5f;
}

/// <summary>
///     A surge of water throws everyone who isn't a friend away from you.
/// </summary>
public sealed partial class CultivationTidalSurgeActionEvent : InstantActionEvent, Content.Shared._KS14.Cultivation.IQiCostEvent
{
    [DataField]
    public float QiCost { get; set; } = 40f;

    [DataField]
    public float Range = 3f;

    [DataField]
    public float ThrowDistance = 4f;

    [DataField]
    public TimeSpan Knockdown = TimeSpan.FromSeconds(2);
}

#endregion

#region Wood

/// <summary>
///     Spring qi mends flesh. Wood Root: it also eases an internal injury, and some of it flows back to you.
/// </summary>
public sealed partial class CultivationSpringMendingActionEvent : EntityTargetActionEvent, Content.Shared._KS14.Cultivation.IQiCostEvent
{
    [DataField]
    public float QiCost { get; set; } = 20f;

    [DataField]
    public float Heal = 20f;
}

/// <summary>
///     Heal yourself and every friend around you.
/// </summary>
public sealed partial class CultivationMyriadSpringActionEvent : InstantActionEvent, Content.Shared._KS14.Cultivation.IQiCostEvent
{
    [DataField]
    public float QiCost { get; set; } = 45f;

    [DataField]
    public float Range = 4f;

    [DataField]
    public float Heal = 25f;
}

#endregion

#region Earth

/// <summary>
///     Root yourself like a mountain: take less damage, but move slowly. Earth Root: harder still.
/// </summary>
public sealed partial class CultivationRootedStanceActionEvent : InstantActionEvent, Content.Shared._KS14.Cultivation.IQiCostEvent
{
    [DataField]
    public float QiCost { get; set; } = 10f;

    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(15);

    [DataField]
    public float DamageMultiplier = 0.75f;

    [DataField]
    public float RootDamageMultiplier = 0.6f;

    [DataField]
    public float SpeedMultiplier = 0.6f;
}

/// <summary>
///     Bring the mountain down: everyone close who isn't a friend is battered and knocked flat.
/// </summary>
public sealed partial class CultivationMountainCollapseActionEvent : InstantActionEvent, Content.Shared._KS14.Cultivation.IQiCostEvent
{
    [DataField]
    public float QiCost { get; set; } = 40f;

    [DataField]
    public float Range = 2f;

    [DataField]
    public float BluntDamage = 15f;

    [DataField]
    public TimeSpan Knockdown = TimeSpan.FromSeconds(3);
}

#endregion
