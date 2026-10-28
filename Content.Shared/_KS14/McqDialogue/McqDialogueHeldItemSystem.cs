using Content.Shared.Hands.EntitySystems;
using Content.Shared.Whitelist;
using Robust.Shared.Network;

namespace Content.Shared._KS14.McqDialogue;

/// <summary>
///     Keeps a dialogue with <see cref="McqDialogueHeldItemComponent"/> open only while its user still holds the item.
///         It answers the engine's checks of whether a UI is still usable: the per-tick one closes the dialogue, and
///         the one on each message stops an answer sent before that tick's check.
/// </summary>
public sealed partial class McqDialogueHeldItemSystem : EntitySystem
{
    [Dependency] private INetManager _netManager = default!;
    [Dependency] private EntityWhitelistSystem _entityWhitelistSystem = default!;
    [Dependency] private SharedHandsSystem _handsSystem = default!;

    [SubscribeLocalEvent]
    private void OnStarted(Entity<McqDialogueHeldItemComponent> entity, ref McqDialogueStartedEvent args)
    {
        if (_handsSystem.GetActiveItem(args.User) is { } heldUid &&
            _entityWhitelistSystem.IsWhitelistPassOrNull(entity.Comp.Whitelist, heldUid))
            entity.Comp.HeldUid = heldUid;
    }

    /// <remarks>
    ///     Run in parallel with other UIs' checks, so it only reads.
    /// </remarks>
    [SubscribeLocalEvent]
    private void OnCheckRange(Entity<McqDialogueHeldItemComponent> entity, ref BoundUserInterfaceCheckRangeEvent args)
    {
        if (!IsHeld(entity, args.Actor))
            args.Result = BoundUserInterfaceRangeResult.Fail;
    }

    [SubscribeLocalEvent]
    private void OnMessageAttempt(Entity<McqDialogueHeldItemComponent> entity, ref BoundUserInterfaceMessageAttempt args)
    {
        // Only answers. Refusing to open it would leave the dialogue entity about with no UI to close; the
        //      per-tick check closes it instead.
        if (args.Message is McqDialogueDataSelectedMessage && !IsHeld(entity, args.Actor))
            args.Cancel();
    }

    private bool IsHeld(Entity<McqDialogueHeldItemComponent> entity, EntityUid userUid)
    {
        // Dialogues are opened by the server, which is the only side that saw what was held then.
        if (_netManager.IsClient)
            return true;

        return entity.Comp.HeldUid is { } heldUid && _handsSystem.GetActiveItem(userUid) == heldUid;
    }
}
