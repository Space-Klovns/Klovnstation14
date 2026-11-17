using Content.Shared.Interaction;
using Content.Shared.Materials;
using Content.Shared.Materials.OreSilo;
using Content.Shared.UserInterface;
using Robust.Client.Timing;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client.Materials;

public sealed partial class MaterialStorageSystem
{
    /// <summary>
    ///     Well past any round trip. The server not agreeing by then means it refused.
    /// </summary>
    private static readonly TimeSpan KsHoldTime = TimeSpan.FromSeconds(2);

    [Dependency] private IClientGameTiming _ksTiming = default!;

    /// <summary>
    ///     Set while reading what the storage really holds, past any hold.
    /// </summary>
    private bool _ksReadingHeldPast;

    private readonly List<ProtoId<MaterialPrototype>> _ksReleased = new();

    /// <summary>
    ///     After a predicted insert: the amounts as they stand, which the next state would otherwise take back down.
    /// </summary>
    private void KsHoldAmounts(EntityUid receiver, EntityUid inserted)
    {
        if (!_ksTiming.IsFirstTimePredicted || !TryComp<PhysicalCompositionComponent>(inserted, out var compositionComponent))
            return;

        var heldComponent = EnsureComp<KsHeldMaterialAmountsComponent>(receiver);
        heldComponent.Until = _ksTiming.CurTime + KsHoldTime;
        heldComponent.InsertedTick = _ksTiming.CurTick;

        foreach (var material in compositionComponent.MaterialComposition.Keys)
        {
            heldComponent.Amounts[material] = GetMaterialAmount(receiver, material);
        }
    }

    protected override void KsReleaseHeldAmounts(EntityUid uid)
    {
        RemCompDeferred<KsHeldMaterialAmountsComponent>(uid);
    }

    /// <summary>
    ///     The inserted stack has gone, so the click that inserted it replays as a click with an empty hand: an activation, which
    ///         opens the machine's window, or closes it if it's open, until the next state puts that back. The window blinks
    ///         every time something goes in. A new click isn't a replay, and goes through.
    /// </summary>
    /// <remarks>The old signature, as <see cref="ActivateInWorldEvent"/> isn't a by-ref event.</remarks>
    [SubscribeLocalEvent(before: [typeof(ActivatableUISystem)])]
    private void OnActivateWhileHeld(EntityUid uid, KsHeldMaterialAmountsComponent component, ActivateInWorldEvent args)
    {
        if (!_ksTiming.IsFirstTimePredicted)
            args.Handled = true;
    }

    // After the silo's, which fills in what a linked machine draws from it.
    [SubscribeLocalEvent(after: [typeof(SharedOreSiloSystem)])]
    private void OnGetStoredWhileHeld(Entity<KsHeldMaterialAmountsComponent> entity, ref GetStoredMaterialsEvent args)
    {
        if (_ksReadingHeldPast)
            return;

        foreach (var (material, amount) in entity.Comp.Amounts)
        {
            args.Materials[material] = Math.Max(args.Materials.GetValueOrDefault(material), amount);
        }
    }

    /// <summary>
    ///     Whether the server has shown what the hold was waiting for. Until its state reaches the insert's tick, what the
    ///         storage holds is this client's own prediction, which already has the insert in it: releasing on that lets the
    ///         next state, from before the insert, take the amount back down for a round trip.
    /// </summary>
    public static bool KsCanRelease(GameTick lastServerTick, GameTick insertedTick, int stored, int held)
    {
        return lastServerTick >= insertedTick && stored >= held;
    }

    // On the tick, and not the frame, so a headless client runs it too.
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_ksTiming.IsFirstTimePredicted)
            return;

        var query = EntityQueryEnumerator<KsHeldMaterialAmountsComponent>();
        while (query.MoveNext(out var uid, out var heldComponent))
        {
            if (_ksTiming.CurTime >= heldComponent.Until)
            {
                RemCompDeferred(uid, heldComponent);
                continue;
            }

            _ksReadingHeldPast = true;
            var stored = GetStoredMaterials(uid);
            _ksReadingHeldPast = false;

            _ksReleased.Clear();
            foreach (var (material, amount) in heldComponent.Amounts)
            {
                if (KsCanRelease(_ksTiming.LastRealTick, heldComponent.InsertedTick, stored.GetValueOrDefault(material), amount))
                    _ksReleased.Add(material);
            }

            foreach (var material in _ksReleased)
            {
                heldComponent.Amounts.Remove(material);
            }

            if (heldComponent.Amounts.Count == 0)
                RemCompDeferred(uid, heldComponent);
        }
    }
}
