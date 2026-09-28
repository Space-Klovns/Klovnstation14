using Content.Shared._KS14.TileEffects;

namespace Content.Shared.Chemistry.Reagent;

public sealed partial class ReagentPrototype
{
    /// <summary>
    ///     Tile effects that are actively executed whenever!
    /// </summary>
    [DataField("tileEffects", serverOnly: true)]
    public KsTileEffect[] KsTileEffects = [];
}
