using Content.Server.Cargo.Systems;
using Content.Server.Chat.Systems;
using Content.Shared.Cargo.Components;
using Content.Shared.Cargo.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Llm.Tools;

/// <summary>
///     Moves credits between one of the sending station's department accounts and Central Command's own budget:
///         a deposit pays from the budget into the account, a withdrawal takes from the account into the budget.
///         Money is never created or destroyed, so a budget spent on one department has to be recovered from
///         another before it can be spent again.
/// </summary>
/// <remarks>
///     The budget lives on the persona's own fax machine (<see cref="KsLlmCentCommBudgetComponent"/>), so it
///         starts afresh with the map every round.
/// </remarks>
public sealed partial class KsLlmTransferFundsToolEffect : KsLlmToolEffect
{
    public const string Deposit = "deposit";
    public const string Withdraw = "withdraw";

    [DataField]
    public string AccountArgument = "account";

    [DataField]
    public string AmountArgument = "amount";

    /// <summary>
    ///     Holds <see cref="Deposit"/> or <see cref="Withdraw"/>.
    /// </summary>
    [DataField]
    public string DirectionArgument = "direction";

    /// <summary>
    ///     What Central Command's budget holds before its first transfer of the round.
    /// </summary>
    [DataField]
    public int StartingBudget = 20000;

    /// <summary>
    ///     Whether every transfer is announced to the station. The crew are told where their money went, and the
    ///         model does not have to remember to tell them.
    /// </summary>
    [DataField]
    public bool Announce = true;

    [DataField]
    public LocId AnnouncementSender = "ks-llm-announcement-sender";

    [DataField]
    public Color AnnouncementColor = Color.Gold;

    public override KsLlmToolOutcome Execute(in KsLlmToolContext context)
    {
        var entityManager = context.EntityManager;
        if (context.StationUid is not { } stationUid
            || !entityManager.TryGetComponent<StationBankAccountComponent>(stationUid, out var stationBankAccountComponent))
            return KsLlmToolOutcome.Error("the sending fax's station has no bank accounts.");

        var cargoSystem = entityManager.System<CargoSystem>();
        var station = new Entity<StationBankAccountComponent?>(stationUid, stationBankAccountComponent);

        var accountName = context.Arguments.GetString(AccountArgument) ?? string.Empty;
        var account = new ProtoId<CargoAccountPrototype>(accountName);
        if (!cargoSystem.TryGetAccount(station, account, out var accountBalance))
            return KsLlmToolOutcome.Error($"the station has no '{accountName}' account.");

        var amount = context.Arguments.GetInt(AmountArgument) ?? 0;
        if (amount <= 0)
            return KsLlmToolOutcome.Error("the amount must be a positive number of credits.");

        if (!entityManager.TryGetComponent<KsLlmCentCommBudgetComponent>(context.RecipientFaxUid, out var budgetComponent))
        {
            budgetComponent = entityManager.AddComponent<KsLlmCentCommBudgetComponent>(context.RecipientFaxUid);
            budgetComponent.Balance = StartingBudget;
        }

        switch (context.Arguments.GetString(DirectionArgument))
        {
            case Deposit:
                if (amount > budgetComponent.Balance)
                    return KsLlmToolOutcome.Error($"the Central Command budget only holds {budgetComponent.Balance} credits.");

                budgetComponent.Balance -= amount;
                cargoSystem.UpdateBankAccount(station, amount, account);
                var depositAnnounced = TryAnnounce(context, stationUid, account, "ks-llm-funds-announcement-deposit", amount);
                return KsLlmToolOutcome.Ok($"Deposited {amount} credits into the {accountName} account, which now holds {accountBalance + amount}. "
                                           + $"The Central Command budget has {budgetComponent.Balance} credits left.{depositAnnounced}");

            case Withdraw:
                if (amount > accountBalance)
                    return KsLlmToolOutcome.Error($"the {accountName} account only holds {accountBalance} credits.");

                budgetComponent.Balance += amount;
                cargoSystem.UpdateBankAccount(station, -amount, account);
                var withdrawalAnnounced = TryAnnounce(context, stationUid, account, "ks-llm-funds-announcement-withdraw", amount);
                return KsLlmToolOutcome.Ok($"Withdrew {amount} credits from the {accountName} account, which now holds {accountBalance - amount}. "
                                           + $"The Central Command budget now holds {budgetComponent.Balance} credits.{withdrawalAnnounced}");

            default:
                return KsLlmToolOutcome.Error($"the direction must be '{Deposit}' or '{Withdraw}'.");
        }
    }

    /// <summary>
    ///     Announces a transfer to the station, if configured to. Returns the sentence to add to the model's result,
    ///         so it knows the crew have already been told and does not announce it a second time.
    /// </summary>
    private string TryAnnounce(in KsLlmToolContext context, EntityUid stationUid, ProtoId<CargoAccountPrototype> account, LocId messageId, int amount)
    {
        if (!Announce)
            return string.Empty;

        var localization = context.Localization;
        var accountDisplayName = context.PrototypeManager.TryIndex(account, out var accountPrototype)
            ? localization.GetString(accountPrototype.Name)
            : account.Id;

        context.EntityManager.System<ChatSystem>().DispatchStationAnnouncement(stationUid,
            localization.GetString(messageId, ("amount", amount), ("account", accountDisplayName)),
            sender: localization.GetString(AnnouncementSender),
            colorOverride: AnnouncementColor);

        return " The station has been told of this transfer by announcement.";
    }
}

/// <summary>
///     Central Command's own money, for <see cref="KsLlmTransferFundsToolEffect"/>. Sits on the persona's fax
///         machine, so it lasts exactly as long as the round's map.
/// </summary>
[RegisterComponent, Access(typeof(KsLlmTransferFundsToolEffect))]
public sealed partial class KsLlmCentCommBudgetComponent : Component
{
    [DataField]
    public int Balance;
}
