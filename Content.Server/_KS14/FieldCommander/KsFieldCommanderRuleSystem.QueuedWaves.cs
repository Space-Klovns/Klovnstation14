namespace Content.Server._KS14.FieldCommander;

public sealed partial class KsFieldCommanderRuleSystem
{
    #region Queued waves

    /// <summary>
    ///     The longest a queued wave's announcement may be.
    /// </summary>
    public const int MaxQueuedAnnouncementLength = 500;

    /// <summary>
    ///     The most purchases of one asset in one queued wave.
    /// </summary>
    public const int MaxQueuedPurchases = 50;

    /// <summary>
    ///     The most waves that may wait in the queue.
    /// </summary>
    public const int MaxQueuedWaves = 20;

    /// <summary>
    ///     Queues a wave of exactly these purchases, sent in place of the next wave the budget would have bought, or
    ///         at once. For admins: everything is checked here, whatever sent it. False, with nothing queued, for an
    ///         unknown asset, a wave with nothing in it, or a full queue. Counts are clamped to
    ///         <see cref="MaxQueuedPurchases"/>, the announcement trimmed to <see cref="MaxQueuedAnnouncementLength"/>.
    /// </summary>
    /// <param name="purchases">Index into <see cref="KsFieldCommanderRuleComponent.Assets"/>, and how many purchases of it.</param>
    /// <param name="announcement">Sent as written, in place of the commander's random taunt. Null or blank for the random one.</param>
    /// <param name="sendNow">Sends it at once, ahead of anything already queued, as a forced wave.</param>
    public bool TryQueueWave(Entity<KsFieldCommanderRuleComponent> rule,
        IReadOnlyList<(int Asset, int Count)> purchases,
        string? announcement,
        bool sendNow = false)
    {
        var component = rule.Comp;
        if (purchases.Count == 0 || (!sendNow && component.QueuedWaves.Count >= MaxQueuedWaves))
            return false;

        var wave = new KsFieldCommanderQueuedWave();
        foreach (var (assetIndex, count) in purchases)
        {
            if (!IsUsableAsset(component, assetIndex))
                return false;

            if (count <= 0)
                continue;

            wave.Purchases.Add(new KsFieldCommanderQueuedPurchase
            {
                Asset = assetIndex,
                Count = Math.Min(count, MaxQueuedPurchases),
            });
        }

        if (wave.Purchases.Count == 0)
            return false;

        if (announcement != null)
        {
            announcement = announcement.Trim();
            if (announcement.Length > MaxQueuedAnnouncementLength)
                announcement = announcement[..MaxQueuedAnnouncementLength];

            wave.Announcement = announcement.Length > 0 ? announcement : null;
        }

        if (!sendNow)
        {
            component.QueuedWaves.Add(wave);
            return true;
        }

        component.QueuedWaves.Insert(0, wave);
        ForceWave(rule);
        return true;
    }

    /// <summary>
    ///     Whether an index names an asset that can still be sent: one that exists, and is not disabled.
    /// </summary>
    private static bool IsUsableAsset(KsFieldCommanderRuleComponent component, int assetIndex)
    {
        return assetIndex >= 0 && assetIndex < component.Assets.Count && !component.Assets[assetIndex].Disabled;
    }

    /// <summary>
    ///     Takes the next queued wave off the queue, if there is one, and fills <paramref name="purchases"/> from it,
    ///         as far as <paramref name="capacity"/> goes: an admin's wave keeps only to the hard cap on units alive,
    ///         not to the one that scales with the defenders.
    /// </summary>
    private bool TryTakeQueuedWave(KsFieldCommanderRuleComponent component, int capacity, List<int> purchases, out string? announcement)
    {
        announcement = null;
        if (component.QueuedWaves.Count == 0)
            return false;

        var wave = component.QueuedWaves[0];
        component.QueuedWaves.RemoveAt(0);
        announcement = string.IsNullOrWhiteSpace(wave.Announcement) ? null : wave.Announcement;

        foreach (var purchase in wave.Purchases)
        {
            // Disabled since it was queued, or never there.
            if (!IsUsableAsset(component, purchase.Asset))
                continue;

            var asset = component.Assets[purchase.Asset];
            for (var i = 0; i < purchase.Count && asset.Count <= capacity; i++)
            {
                purchases.Add(purchase.Asset);
                capacity -= asset.Count;
            }
        }

        return true;
    }

    /// <summary>
    ///     Sends a queued wave's own announcement as the commander's taunt, exactly as written.
    /// </summary>
    private void AnnounceQueuedWave(KsFieldCommanderRuleComponent component, string announcement)
    {
        _chatSystem.DispatchGlobalAnnouncement(announcement,
            Loc.GetString(component.Sender),
            playSound: false,
            colorOverride: component.AnnouncementColor);
    }

    #endregion
}
