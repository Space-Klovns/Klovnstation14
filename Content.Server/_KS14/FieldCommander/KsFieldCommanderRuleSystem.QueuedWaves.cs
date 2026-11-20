using Content.Server.Chat.Managers;

namespace Content.Server._KS14.FieldCommander;

public sealed partial class KsFieldCommanderRuleSystem
{
    #region Queued waves

    [Dependency] private IChatManager _chatManager = default!;

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
    ///     The most purchases one queued wave may list.
    /// </summary>
    public const int MaxQueuedWaveEntries = 32;

    /// <summary>
    ///     Queues a wave of exactly these purchases, sent in place of the next wave the budget would have bought, or
    ///         at once. For admins: everything is checked here, whatever sent it. False, with nothing queued, for an
    ///         unknown asset, a wave with nothing in it or more than <see cref="MaxQueuedWaveEntries"/> purchases, or a
    ///         full queue, a wave sent now included. Counts are clamped to
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
        // A wave sent now goes into the queue too, and stays there if it cannot go.
        if (purchases.Count == 0 || purchases.Count > MaxQueuedWaveEntries || component.QueuedWaves.Count >= MaxQueuedWaves)
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
    ///     Takes a queued wave out of the queue. For admins. False for an index with no wave.
    /// </summary>
    public bool TryRemoveQueuedWave(Entity<KsFieldCommanderRuleComponent> rule, int index)
    {
        if (index < 0 || index >= rule.Comp.QueuedWaves.Count)
            return false;

        rule.Comp.QueuedWaves.RemoveAt(index);
        return true;
    }

    /// <summary>
    ///     Takes the next queued wave that has anything to send now off the queue and fills <paramref name="purchases"/>
    ///         from it, as far as <paramref name="capacity"/> goes: an admin's wave keeps only to the hard cap on units
    ///         alive, not to the one that scales with the defenders. A wave that never could send anything (every asset
    ///         in it disabled since, or bigger than that cap) is dropped, and the admins told. One that only has no room
    ///         now stays at the front, and false lets the budget's wave go in its place: waiting for room must not hold
    ///         every wave back.
    /// </summary>
    private bool TryTakeQueuedWave(Entity<KsFieldCommanderRuleComponent> rule, int capacity, List<int> purchases, out KsFieldCommanderQueuedWave? taken)
    {
        taken = null;
        var component = rule.Comp;

        while (component.QueuedWaves.Count > 0)
        {
            var wave = component.QueuedWaves[0];
            if (!CanEverSend(component, wave))
            {
                component.QueuedWaves.RemoveAt(0);
                Log.Warning($"{ToPrettyString(rule.Owner)} dropped a queued wave that could never be sent: every asset in it is disabled, or brings more units than the hard cap of {component.HardMaxAlive}.");
                _chatManager.SendAdminAlert(Loc.GetString("ks-fieldcommander-admin-queue-dropped", ("cap", component.HardMaxAlive)));
                continue;
            }

            var before = purchases.Count;
            foreach (var purchase in wave.Purchases)
            {
                if (!IsUsableAsset(component, purchase.Asset))
                    continue;

                var asset = component.Assets[purchase.Asset];
                for (var i = 0; i < purchase.Count && asset.Count <= capacity; i++)
                {
                    purchases.Add(purchase.Asset);
                    capacity -= asset.Count;
                }
            }

            if (purchases.Count == before)
                return false;

            component.QueuedWaves.RemoveAt(0);
            taken = wave;
            return true;
        }

        return false;
    }

    /// <summary>
    ///     Whether a queued wave has anything in it that could be sent once there is room: an asset still in use, that
    ///         brings at least one unit and no more than the hard cap on units alive.
    /// </summary>
    private static bool CanEverSend(KsFieldCommanderRuleComponent component, KsFieldCommanderQueuedWave wave)
    {
        foreach (var purchase in wave.Purchases)
        {
            if (purchase.Count <= 0 || !IsUsableAsset(component, purchase.Asset))
                continue;

            var count = component.Assets[purchase.Asset].Count;
            if (count > 0 && count <= component.HardMaxAlive)
                return true;
        }

        return false;
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
