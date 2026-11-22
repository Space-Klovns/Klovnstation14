using Content.Shared._KS14.Perks;

namespace Content.Shared._RMC14.Vendors;

/// <summary>
///     Entries only some players may take (<see cref="CMVendorEntry.KsRequiredPerk"/>).
/// </summary>
public abstract partial class SharedCMAutomatedVendorSystem
{
    [Dependency] private SharedKsPerkSystem _ksPerkSystem = default!;

    /// <summary>
    ///     Whether <paramref name="user"/> may take <paramref name="entry"/> at all, before stock, points or choices.
    /// </summary>
    public bool KsMeetsEntryRequirements(EntityUid? user, CMVendorEntry entry)
    {
        if (entry.KsRequiredPerk is not { } requiredPerk)
            return true;

        return user is { } userUid && _ksPerkSystem.HasPerk(userUid, requiredPerk);
    }
}
