using Content.Shared.Administration;
using Content.Shared.CCVar.CVarAccess;
using Robust.Shared.Configuration;

namespace Content.Shared._KS14.CCVar;

public sealed partial class KsCCVars
{
    /// <summary>
    ///     Probability that an eligible station map is replaced by its configured Saturn-cloud variant.
    ///     Zero disables the setting variation. Values outside zero to one are clamped by the selection system.
    /// </summary>
    [CVarControl(AdminFlags.Fun)]
    public static readonly CVarDef<float> SaturnCloudSettingProbability =
        CVarDef.Create("klovn.saturn_clouds.probability", 0.2f, CVar.ARCHIVE | CVar.SERVERONLY);
}
