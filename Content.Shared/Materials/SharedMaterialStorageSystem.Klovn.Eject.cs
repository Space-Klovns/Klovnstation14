using System.Linq;
using Content.Shared.ActionBlocker;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Stacks;

namespace Content.Shared.Materials;

public abstract partial class SharedMaterialStorageSystem
{
    [Dependency] private ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;

    /// <summary>
    ///     Takes sheets out of the storage, into the player's hands if they have room, and onto the machine if they don't.
    ///         Moved here from the server so that it's predicted: the client raises this with <c>RaisePredictiveEvent</c>, so it
    ///         runs again on every re-prediction, and the sheets it spawns are engine predicted spawns, which the server's
    ///         copy replaces.
    /// </summary>
    [EventSubscription]
    private void OnEjectMessage(EjectMaterialMessage msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } playerUid)
            return;

        var uid = GetEntity(msg.Entity);

        if (!TryComp<MaterialStorageComponent>(uid, out var component))
            return;

        // The range matters: nothing else stops a client asking for sheets from a machine across the station, into its hand.
        if (!_actionBlocker.CanInteract(playerUid, uid) || !_interaction.InRangeUnobstructed(playerUid, uid, popup: false))
            return;

        if (!component.CanEjectStoredMaterials || !ProtoMan.TryIndex<MaterialPrototype>(msg.Material, out var material))
            return;

        if (material.StackEntity is not { } stackEntity)
            return;

        if (!ProtoMan.Index(stackEntity).TryGetComponent<PhysicalCompositionComponent>(out var composition, Factory))
            return;

        var volumePerSheet = composition.MaterialComposition.FirstOrDefault(kvp => kvp.Key == msg.Material).Value;
        var sheets = Math.Min(msg.SheetsToExtract, _heap.GetMaxCount(stackEntity));
        var volume = sheets * volumePerSheet;

        if (volume <= 0 || !TryChangeMaterialAmount(uid, msg.Material, -volume))
            return;

        KsReleaseHeldAmounts(uid);

        var stackUid = PredictedSpawnAtPosition(stackEntity, Transform(uid).Coordinates);
        _heap.SetCount((stackUid, null), sheets);
        GiveEjectedStack(stackUid, playerUid);
    }

    /// <summary>
    ///     A predicted insert holds the shown amounts up on the client, which would hide this taking them down.
    /// </summary>
    protected virtual void KsReleaseHeldAmounts(EntityUid uid)
    {
    }

    /// <summary>
    ///     Into a stack the player already holds, then a free hand. Whatever is left stays where it was spawned, on the machine,
    ///         joining any sheets already there.
    /// </summary>
    private void GiveEjectedStack(EntityUid stackUid, EntityUid playerUid)
    {
        if (!TryComp<StackComponent>(stackUid, out var stackComponent))
            return;

        foreach (var heldUid in _hands.EnumerateHeld(playerUid))
        {
            _heap.TryMergeStacks((stackUid, stackComponent), heldUid, out _);

            if (stackComponent.Count == 0)
                return;
        }

        if (!_hands.TryPickupAnyHand(playerUid, stackUid))
            _heap.TryMergeToContacts((stackUid, stackComponent, null));
    }
}
