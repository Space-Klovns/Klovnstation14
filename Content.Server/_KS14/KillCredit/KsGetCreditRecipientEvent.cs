using Robust.Shared.Network;

namespace Content.Server._KS14.KillCredit;

/// <summary>
///     Raised on whatever dealt damage, to ask which player is paid for it: a player's body answers with its player, a
///         turret with whoever set it up, a bullet with whoever its shooter answers to. Nobody, if nothing answers.
///         Ask through <see cref="KsKillCreditSystem.GetRecipient(EntityUid)"/>.
/// </summary>
/// <param name="Recipient">The player to pay. A handler fills it only if it is still null.</param>
/// <param name="Depth">How many entities have passed the question on already, so that a loop of them ends.</param>
[ByRefEvent]
public record struct KsGetCreditRecipientEvent(NetUserId? Recipient = null, int Depth = 0);
