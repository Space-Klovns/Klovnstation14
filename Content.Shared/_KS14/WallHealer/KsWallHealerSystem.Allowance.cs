namespace Content.Shared._KS14.WallHealer;

public sealed partial class KsWallHealerSystem
{
    /// <summary>
    ///     The allowance on an ID card, added on the server the first time a station needs it. A client can't add a
    ///         networked component, so it gets null until the server's arrives.
    /// </summary>
    private Entity<KsWallHealerAllowanceComponent>? ResolveAllowance(EntityUid idCard)
    {
        if (TryComp<KsWallHealerAllowanceComponent>(idCard, out var allowanceComponent))
            return (idCard, allowanceComponent);

        if (_netManager.IsClient)
            return null;

        allowanceComponent = EnsureComp<KsWallHealerAllowanceComponent>(idCard);
        allowanceComponent.SpentAt = _gameTiming.CurTime;
        Dirty(idCard, allowanceComponent);
        return (idCard, allowanceComponent);
    }

    /// <summary>
    ///     How much has been spent, after what has recovered since it was last spent.
    /// </summary>
    private float GetSpentAllowance(Entity<KsWallHealerAllowanceComponent> card)
    {
        if (card.Comp.RecoveryTime <= TimeSpan.Zero)
            return 0f;

        var recoveredFraction = (float)((_gameTiming.CurTime - card.Comp.SpentAt) / card.Comp.RecoveryTime);
        return MathF.Max(card.Comp.Spent - card.Comp.Limit * MathF.Max(recoveredFraction, 0f), 0f);
    }

    public float GetRemainingAllowance(Entity<KsWallHealerAllowanceComponent> card)
    {
        return MathF.Max(card.Comp.Limit - GetSpentAllowance(card), 0f);
    }

    private void SpendAllowance(Entity<KsWallHealerAllowanceComponent> card, float amount)
    {
        card.Comp.Spent = GetSpentAllowance(card) + amount;
        card.Comp.SpentAt = _gameTiming.CurTime;
        Dirty(card);
    }
}
