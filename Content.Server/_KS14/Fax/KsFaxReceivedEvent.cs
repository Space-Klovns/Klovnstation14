using Content.Shared.Fax.Components;

namespace Content.Server._KS14.Fax;

/// <summary>
///     Raised on a fax machine when it accepts a printout for printing, whoever sent it.
/// </summary>
/// <param name="Printout">What will be printed.</param>
/// <param name="FromAddress">The device-network address of the sending fax, when it came over the network.</param>
[ByRefEvent]
public readonly record struct KsFaxReceivedEvent(FaxPrintout Printout, string? FromAddress);
