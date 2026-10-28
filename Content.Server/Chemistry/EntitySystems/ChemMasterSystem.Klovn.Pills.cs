using System.Diagnostics.CodeAnalysis;
using Content.Server.Chemistry.Components;
using Content.Shared._KS14.PopupLocalization;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Storage;
using Content.Shared.Tag;
using Robust.Shared.Audio;

namespace Content.Server.Chemistry.EntitySystems;

public sealed partial class ChemMasterSystem
{
    [Dependency] private TagSystem _ksPillTagSystem = default!;

    // Match SharedStorageSystem's insertion sound parameters when committing the batch.
    private static readonly AudioParams KsPillInsertAudioParams = AudioParams.Default.WithMaxDistance(7f).WithVolume(-2f);

    /// <summary>
    /// Validate the client batch before allocating entities or consuming reagents.
    /// Prepare all storage insertions before withdrawal so a veto cannot leave loose pills or lose mixture.
    /// </summary>
    private bool KsTryPreparePills(
        Entity<ChemMasterComponent> chemMasterEntity,
        Entity<StorageComponent> containerEntity,
        ChemMasterCreatePillsMessage message,
        [NotNullWhen(true)] out List<EntityUid>? pills,
        [NotNullWhen(true)] out Solution? withdrawal,
        out FixedPoint2 dosage)
    {
        pills = null;
        withdrawal = null;
        dosage = FixedPoint2.Zero;

        // Pills occupy one cell. Match the UI's capacity limit using authoritative storage state.
        var freeArea = containerEntity.Comp.Grid.GetArea() - _storageSystem.GetCumulativeItemAreas((containerEntity.Owner, containerEntity.Comp));
        if (message.Number == 0 || freeArea <= 0 || message.Number > freeArea)
            return false;

        if (message.Dosage == 0 || message.Dosage > chemMasterEntity.Comp.PillDosageLimit)
            return false;

        if (message.Label == null || message.Label.Length > SharedChemMaster.LabelMaxLength)
            return false;

        // Widen before multiplication, then check the int-backed fixed-point representation as well.
        var neededUnits = (ulong) message.Dosage * message.Number;
        if (neededUnits > (ulong) FixedPoint2.MaxValue.Int())
            return false;

        var needed = FixedPoint2.New((int) neededUnits);
        dosage = FixedPoint2.New((int) message.Dosage);
        // Reject missing or insufficient sources without creating entities. Withdrawal rechecks after insertion.
        if (!KsTryResolveDrawSource(chemMasterEntity, needed, message.Actor, out _, out _))
            return false;

        var prepared = new List<EntityUid>((int) message.Number);
        try
        {
            for (var i = 0; i < (int) message.Number; i++)
            {
                var pillUid = Spawn(PillPrototypeId, Transform(containerEntity).Coordinates);
                prepared.Add(pillUid);
                if (!_storageSystem.Insert(containerEntity, pillUid, out _, user: message.Actor, storageComp: containerEntity.Comp, playSound: false))
                    return false;
            }

            if (!WithdrawFromSource(chemMasterEntity, needed, message.Actor, out withdrawal))
                return false;

            pills = prepared;
            // Prepared insertions are silent: rejected batches must not leave success sounds behind.
            if (!_ksPillTagSystem.HasTag(message.Actor, containerEntity.Comp.SilentStorageUserTag))
            {
                foreach (var _ in prepared)
                    _audioSystem.PlayPredicted(containerEntity.Comp.StorageInsertSound, containerEntity, message.Actor, audioParams: KsPillInsertAudioParams);
            }

            return true;
        }
        finally
        {
            if (pills == null)
            {
                foreach (var pillUid in prepared)
                    Del(pillUid);
            }
        }
    }

    /// <summary>
    /// Resolve the selected source and report the existing ChemMaster errors without consuming reagents.
    /// Shared by pill preflight and the final withdrawal, including bottle output.
    /// </summary>
    private bool KsTryResolveDrawSource(
        Entity<ChemMasterComponent> chemMasterEntity,
        FixedPoint2 neededVolume,
        EntityUid? user,
        [NotNullWhen(true)] out Solution? solution,
        out Entity<SolutionComponent>? sourceEntity)
    {
        solution = null;
        sourceEntity = null;

        switch (chemMasterEntity.Comp.DrawSource)
        {
            case ChemMasterDrawSource.Internal:
                if (!_solutionContainerSystem.TryGetSolution(chemMasterEntity.Owner, SharedChemMaster.BufferSolutionName, out _, out solution))
                    return false;

                if (solution.Volume == 0)
                {
                    if (user is { } uid)
                        _popupSystem.PopupCursor(KsPopupMessage.Create("chem-master-window-buffer-empty-text"), uid);

                    return false;
                }
                if (neededVolume > solution.Volume)
                {
                    if (user is { } uid)
                        _popupSystem.PopupCursor(KsPopupMessage.Create("chem-master-window-buffer-low-text"), uid);

                    return false;
                }

                break;

            case ChemMasterDrawSource.External:
                if (_itemSlotsSystem.GetItemOrNull(chemMasterEntity, SharedChemMaster.InputSlotName) is not { } containerUid)
                {
                    if (user.HasValue)
                        _popupSystem.PopupCursor(KsPopupMessage.Create("chem-master-window-no-beaker-text"), user.Value);
                    return false;
                }

                if (!_solutionContainerSystem.TryGetFitsInDispenser(containerUid, out sourceEntity, out solution))
                    return false;

                if (solution.Volume == 0)
                {
                    if (user is { } uid)
                        _popupSystem.PopupCursor(KsPopupMessage.Create("chem-master-window-beaker-empty-text"), uid);

                    return false;
                }
                if (neededVolume > solution.Volume)
                {
                    if (user is { } uid)
                        _popupSystem.PopupCursor(KsPopupMessage.Create("chem-master-window-beaker-low-text"), uid);

                    return false;
                }

                break;

            default:
                return false;
        }

        return true;
    }
}
