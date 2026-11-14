using Robust.Shared.Prototypes;

namespace Content.Shared._RMC14.Vendors;

public sealed partial class CMVendorSection
{
    /// <summary>
    ///     An entry of this same vendor that is vended along with whatever is taken from this section, if the player
    ///         hasn't already had it. The entry's own section decides how often that can be: with <c>choices</c>, the
    ///         player's count for that choice id is spent, so a player gets it once however they come by it.
    /// </summary>
    [DataField]
    public EntProtoId? KsAlsoVend;
}
