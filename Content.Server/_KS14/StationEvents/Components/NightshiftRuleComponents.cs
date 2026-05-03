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
    ///     Affected lights+bulbs and their original colors.
    /// </summary>
    [DataField]
    public HashSet<EntityUid> Lights = [];

    [DataField]
    public HashSet<string> DangerousAlertLevels = [];
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
