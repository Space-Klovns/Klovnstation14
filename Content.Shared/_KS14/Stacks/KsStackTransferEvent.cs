namespace Content.Shared._KS14.Stacks;

/// <summary>
///     Raised on the receiving stack when units are about to move into it from another stack, before either count
///         changes.
/// </summary>
[ByRefEvent]
public readonly record struct KsStackTransferEvent(EntityUid Donor, EntityUid Recipient, int Amount);
