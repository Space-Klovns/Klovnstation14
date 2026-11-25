using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Cultivation;

/// <summary>
///     Techniques that grow out of an earlier one. When the stronger one is learned it takes the weaker one's place on the
///         bar and the weaker one is put away for good, so the bar doesn't fill with arts that have been outgrown.
/// </summary>
public static class CultivationUpgrades
{
    /// <summary>
    ///     The stronger technique, and the one it replaces.
    /// </summary>
    public static readonly (EntProtoId Upgrade, EntProtoId Replaces)[] Chains =
    {
        // Spring Mending's heal, widened to every friend around the one healed.
        ("ActionCultivationMyriadSpring", "ActionCultivationSpringMending"),
    };

    /// <summary>
    ///     What replaces this technique, if anything.
    /// </summary>
    public static EntProtoId? UpgradeOf(EntProtoId action)
    {
        foreach (var (upgrade, replaces) in Chains)
        {
            if (replaces == action)
                return upgrade;
        }

        return null;
    }
}
