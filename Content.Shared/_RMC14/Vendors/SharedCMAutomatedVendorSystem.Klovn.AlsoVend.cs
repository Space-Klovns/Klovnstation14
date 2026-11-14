using System.Diagnostics.CodeAnalysis;
using Content.Shared._KS14.Random.Helpers;
using Robust.Shared.Prototypes;

namespace Content.Shared._RMC14.Vendors;

/// <summary>
///     Vends one entry along with another (<see cref="CMVendorSection.KsAlsoVend"/>): a loadout that comes with the
///         base gear, the base gear only the first time.
/// </summary>
public abstract partial class SharedCMAutomatedVendorSystem
{
    /// <summary>
    ///     Whether taking from <paramref name="section"/> would also vend something the player has yet to get, for
    ///         the UI to say so.
    /// </summary>
    public bool KsAlsoVendPending(CMAutomatedVendorComponent vendor, CMVendorSection section, CMVendorUserComponent? user)
    {
        if (section.KsAlsoVend is not { } alsoVendId ||
            !KsTryFindEntry(vendor, alsoVendId, out var alsoVendSection, out var alsoVendEntry))
        {
            return false;
        }

        return KsCanAlsoVend(alsoVendSection, alsoVendEntry, user);
    }

    private static bool KsCanAlsoVend(CMVendorSection alsoVendSection, CMVendorEntry alsoVendEntry, CMVendorUserComponent? user)
    {
        if (alsoVendEntry.Amount is <= 0)
            return false;

        if (alsoVendSection.Choices is not { } choices)
            return true;

        return user == null ? choices.Amount > 0 : user.Choices.GetValueOrDefault(choices.Id) < choices.Amount;
    }

    private static bool KsTryFindEntry(
        CMAutomatedVendorComponent vendor,
        EntProtoId entryId,
        [NotNullWhen(true)] out CMVendorSection? foundSection,
        [NotNullWhen(true)] out CMVendorEntry? foundEntry)
    {
        foreach (var section in vendor.Sections)
        {
            foreach (var entry in section.Entries)
            {
                if (entry.Id != entryId)
                    continue;

                foundSection = section;
                foundEntry = entry;
                return true;
            }
        }

        foundSection = null;
        foundEntry = null;
        return false;
    }

    /// <summary>
    ///     Called once a vend from <paramref name="section"/> has passed every check. Spends the player's count for
    ///         the along-vended entry's choice (that count lives on the player's mob, so it lasts one life) and
    ///         vends it, before the entry that was picked so that the pick replaces what it should.
    /// </summary>
    private void KsVendAlongside(Entity<CMAutomatedVendorComponent> vendor, EntityUid actor, CMVendorSection section)
    {
        if (section.KsAlsoVend is not { } alsoVendId)
            return;

        if (!KsTryFindEntry(vendor.Comp, alsoVendId, out var alsoVendSection, out var alsoVendEntry))
        {
            Log.Error($"{ToPrettyString(vendor)} section {section.Name} also vends {alsoVendId}, which it doesn't list.");
            return;
        }

        var user = CompOrNull<CMVendorUserComponent>(actor);
        if (!KsCanAlsoVend(alsoVendSection, alsoVendEntry, user))
            return;

        if (alsoVendSection.Choices is { } choices)
        {
            user = EnsureComp<CMVendorUserComponent>(actor);
            user.Choices[choices.Id] = user.Choices.GetValueOrDefault(choices.Id) + 1;
            Dirty(actor, user);
        }

        if (alsoVendEntry.Amount != null)
        {
            alsoVendEntry.Amount--;
            Dirty(vendor);
            AmountUpdated(vendor, alsoVendEntry);
        }

        if (!ProtoMan.TryIndex(alsoVendEntry.Id, out var entity))
        {
            Log.Error($"Tried to vend non-existent entity: {alsoVendEntry.Id}");
            return;
        }

        var predictedRandom = KsSharedRandomExtensions.RandomWithHashCodeCombinedSeed(
            KsSharedRandomExtensions.GetNetId(vendor.Owner, EntityManager),
            (int)_timing.CurTick.Value
        );
        var offset = predictedRandom.NextVector2Box(
            vendor.Comp.MinOffset.X,
            vendor.Comp.MinOffset.Y,
            vendor.Comp.MaxOffset.X,
            vendor.Comp.MaxOffset.Y);

        for (var i = 0; i < alsoVendEntry.Spawn; i++)
        {
            if (entity.TryGetComponent(out CMVendorBundleComponent? bundle, _compFactory))
                KsVendAll(vendor, actor, bundle.Bundle, offset, alsoVendEntry.ReplaceSlot);
            else
                KsVendAll(vendor, actor, [alsoVendEntry.Id], offset, alsoVendEntry.ReplaceSlot);
        }
    }
}
