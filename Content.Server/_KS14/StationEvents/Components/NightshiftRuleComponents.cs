using Content.Server._KS14.AutomaticNightshift;
using Content.Server._KS14.StationEvents.Events;

namespace Content.Server._KS14.StationEvents.Components;

[RegisterComponent, Access(typeof(NightshiftRule), typeof(AutomaticNightshiftSystem))]
public sealed partial class NightshiftRuleComponent : Component
{
    [DataField]
    public Color Color = Color.DarkSlateBlue;

    [DataField]
    public EntityUid StationUid = EntityUid.Invalid;


    [DataField]
    public bool Enabled = false;

    /// <summary>
    ///     Affected lights.
    /// </summary>
    [DataField]
    public HashSet<EntityUid> Lights = [];

    /// <summary>
    ///     Affected bulbs.
    /// </summary>
    [DataField]
    public HashSet<EntityUid> Bulbs = [];

    [DataField]
    public HashSet<string> DangerousAlertLevels = [];
}

/// <summary>
///     Added to a PoweredLight affected by nightshift.
/// </summary>
[RegisterComponent, Access(typeof(NightshiftRule))]
public sealed partial class NightshiftLightComponent : Component
{
    [DataField]
    public EntityUid? OwningRuleUid = null;

    [DataField]
    public Color NewColor = Color.Red;
}


/// <summary>
///     Added to a bulb affected by nightshift.
/// </summary>
[RegisterComponent, Access(typeof(NightshiftRule))]
public sealed partial class NightshiftBulbComponent : Component
{
    [DataField]
    public EntityUid? OwningRuleUid = null;

    [DataField]
    public Color OriginalColor = Color.White;
}

/// <summary>
///     Added to bulbs that should never be affected by nightshift.
/// </summary>
[RegisterComponent, Access(typeof(NightshiftRule))]
public sealed partial class NightshiftExemptBulbComponent : Component;
