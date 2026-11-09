using Content.Server._KS14.NPC.Doors;
using Content.Server._KS14.NPC.Hands;
using Content.Shared._KS14.Chain;
using Content.Shared._KS14.LinkedChain;
using Content.Shared.Explosion.Components;
using Content.Shared.Interaction;
using Content.Shared.Sticky.Components;
using Content.Shared.Trigger.Components;
using Content.Shared.Whitelist;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.Server._KS14.NPC.Charges;

/// <summary>
///     How a charge gets set off.
/// </summary>
public enum NpcChargeMethod : byte
{
    /// <summary>
    ///     By pressing the activator wired to it: when it suits, and from no further away than the wire.
    /// </summary>
    Wired,

    /// <summary>
    ///     By its own timer, started as it goes on. Everyone has however long that is, and no say in the matter.
    /// </summary>
    Timed,
}

/// <param name="ChargeUid">The charge.</param>
/// <param name="ActivatorUid">What sets it off, if it is wired.</param>
/// <param name="Method">How it gets set off.</param>
/// <param name="Fuse">How long it takes once it is on, if it is timed.</param>
public readonly record struct NpcCarriedCharge(EntityUid ChargeUid, EntityUid? ActivatorUid, NpcChargeMethod Method, TimeSpan Fuse);

/// <summary>
///     The one way an NPC uses a breaching charge, as a player would: a wire goes from the activator to the charge, the charge
///         goes on the target, and the activator gets pressed. Or for a charge with a timer, it just goes on the target.
///     <para>
///         What makes something a charge is its components, so an NPC given C4 knows what to do with it, and whether a charge
///             sticks to a target is up to the charge's own whitelist, as it is for a player.
///     </para>
/// </summary>
public sealed partial class NpcChargeSystem : EntitySystem
{
    [Dependency] private ChainSystem _chainSystem = default!;
    [Dependency] private EntityWhitelistSystem _entityWhitelistSystem = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private NpcDoorSystem _npcDoorSystem = default!;
    [Dependency] private NpcHandsSystem _npcHandsSystem = default!;
    [Dependency] private SharedInteractionSystem _interactionSystem = default!;

    [Dependency] private EntityQuery<ExplosiveComponent> _explosiveQuery = default!;
    [Dependency] private EntityQuery<LinkedChainEndComponent> _chainEndQuery = default!;
    [Dependency] private EntityQuery<LinkedChainStartComponent> _chainStartQuery = default!;
    [Dependency] private EntityQuery<NpcPlantingChargeComponent> _plantingQuery = default!;
    [Dependency] private EntityQuery<PhysicsComponent> _physicsQuery = default!;
    [Dependency] private EntityQuery<StickyComponent> _stickyQuery = default!;
    [Dependency] private EntityQuery<TimerTriggerComponent> _timerTriggerQuery = default!;

    /// <summary>
    ///     How long the sticking can take before something is taken to be wrong.
    /// </summary>
    private static readonly TimeSpan PlantTimeout = TimeSpan.FromSeconds(8);

    /// <summary>
    ///     How much longer than the charge takes to stick to leave before having another go.
    /// </summary>
    private static readonly TimeSpan PlantRetryMargin = TimeSpan.FromSeconds(0.5);

    /// <summary>
    ///     How fast a planter can still be going to start sticking. It takes standing still, and any faster and it is cut short.
    /// </summary>
    private const float MaxPlantSpeed = 0.1f;

    private readonly List<EntityUid> _carried = new();

    /// <summary>
    ///     Whether <paramref name="chargeUid"/> would go on <paramref name="targetUid"/>. Changes nothing.
    /// </summary>
    public bool CanStick(EntityUid chargeUid, EntityUid targetUid)
    {
        return _stickyQuery.TryComp(chargeUid, out var stickyComponent) &&
            !_entityWhitelistSystem.IsWhitelistFail(stickyComponent.Whitelist, targetUid) &&
            !_entityWhitelistSystem.IsWhitelistPass(stickyComponent.Blacklist, targetUid);
    }

    /// <summary>
    ///     A charge <paramref name="npcUid"/> carries and can set off, for <paramref name="targetUid"/> if given. Wired before
    ///         timed. A wired charge with no activator is no use to anyone. Changes nothing.
    /// </summary>
    public bool TryGetCharge(EntityUid npcUid, EntityUid? targetUid, out NpcCarriedCharge charge)
    {
        charge = default;

        _npcHandsSystem.GetCarried(npcUid, _carried);

        EntityUid? activatorUid = null;

        foreach (var itemUid in _carried)
        {
            if (_chainStartQuery.HasComp(itemUid))
                activatorUid = itemUid;
        }

        var found = false;

        foreach (var itemUid in _carried)
        {
            if (!IsStickableExplosive(itemUid, targetUid))
                continue;

            if (_chainEndQuery.HasComp(itemUid))
            {
                if (activatorUid == null)
                    continue;

                charge = new NpcCarriedCharge(itemUid, activatorUid, NpcChargeMethod.Wired, TimeSpan.Zero);
                return true;
            }

            if (!found && _timerTriggerQuery.TryComp(itemUid, out var timerTriggerComponent))
            {
                charge = new NpcCarriedCharge(itemUid, null, NpcChargeMethod.Timed, timerTriggerComponent.Delay);
                found = true;
            }
        }

        return found;
    }

    /// <summary>
    ///     Whether <paramref name="itemUid"/> is an explosive that can still be stuck on something, on
    ///         <paramref name="targetUid"/> if given.
    /// </summary>
    private bool IsStickableExplosive(EntityUid itemUid, EntityUid? targetUid)
    {
        return _stickyQuery.TryComp(itemUid, out var stickyComponent) &&
            stickyComponent.StuckTo == null &&
            _explosiveQuery.HasComp(itemUid) &&
            (targetUid is not { } target || CanStick(itemUid, target));
    }

    /// <summary>
    ///     Takes <paramref name="chargeUid"/> out and wires it up if it needs wiring, ready for <see cref="UpdatePlant"/> to stick
    ///         it on. Returns whether it is ready. Whether it went on is <see cref="IsPlanted"/>. Put things back with
    ///         <see cref="StopPlant"/>, however it goes.
    /// </summary>
    public bool TryStartPlant(EntityUid npcUid, EntityUid chargeUid)
    {
        if (_plantingQuery.HasComp(npcUid) || !_npcHandsSystem.CanTakeOut(npcUid, chargeUid))
            return false;

        if (!_npcHandsSystem.TryTakeOut(npcUid, chargeUid, out var takenOut))
            return false;

        var plantingComponent = AddComp<NpcPlantingChargeComponent>(npcUid);
        plantingComponent.TakenOut = takenOut;
        plantingComponent.GiveUpAt = _gameTiming.CurTime + PlantTimeout;

        if (TryWire(npcUid, chargeUid))
            return true;

        StopPlant(npcUid);
        return false;
    }

    /// <summary>
    ///     Sticks the charge on, the moment <paramref name="npcUid"/> has come to a stop: it takes a moment, and moving cuts it
    ///         short. So does being shoved, or a wire tugging, so whatever gets cut short gets another go. Nothing says when
    ///         one has been, and it is only ever worth trying again once the last would have finished anyway.
    /// </summary>
    public void UpdatePlant(EntityUid npcUid, EntityUid targetUid, EntityUid chargeUid)
    {
        if (!_plantingQuery.TryComp(npcUid, out var plantingComponent) ||
            _gameTiming.CurTime < plantingComponent.NextAttemptAt ||
            _physicsQuery.TryComp(npcUid, out var physicsComponent) && physicsComponent.LinearVelocity.Length() > MaxPlantSpeed)
            return;

        var stickDelay = _stickyQuery.TryComp(chargeUid, out var stickyComponent) ? stickyComponent.StickDelay : TimeSpan.Zero;
        plantingComponent.NextAttemptAt = _gameTiming.CurTime + stickDelay + PlantRetryMargin;

        _interactionSystem.InteractUsing(npcUid, chargeUid, targetUid, Transform(targetUid).Coordinates);
    }

    /// <summary>
    ///     Wires <paramref name="chargeUid"/> to the activator <paramref name="npcUid"/> carries. A gun in one hand and the charge
    ///         in the other leave none for the activator, so it stays where it is carried until it is wanted: the charge's own
    ///         handling of being used on is all that wires them up, and that asks nothing of the hands. Nothing to do for a
    ///         charge that is wired already or takes no wire.
    /// </summary>
    private bool TryWire(EntityUid npcUid, EntityUid chargeUid)
    {
        if (!_chainEndQuery.TryComp(chargeUid, out var chainEndComponent) || chainEndComponent.StartUid != null)
            return true;

        if (!TryGetCharge(npcUid, null, out var charge) || charge.ActivatorUid is not { } activatorUid)
            return false;

        _interactionSystem.InteractUsing(npcUid, activatorUid, chargeUid, Transform(npcUid).Coordinates, checkCanInteract: false, checkCanUse: false);

        return chainEndComponent.StartUid == activatorUid;
    }

    /// <summary>
    ///     Whether <paramref name="chargeUid"/> is stuck on <paramref name="targetUid"/>.
    /// </summary>
    public bool IsPlanted(EntityUid chargeUid, EntityUid targetUid)
    {
        return _stickyQuery.TryComp(chargeUid, out var stickyComponent) && stickyComponent.StuckTo == targetUid;
    }

    /// <summary>
    ///     Whether <paramref name="npcUid"/> is still trying to stick a charge on.
    /// </summary>
    public bool IsPlanting(EntityUid npcUid)
    {
        return _plantingQuery.TryComp(npcUid, out var plantingComponent) && _gameTiming.CurTime < plantingComponent.GiveUpAt;
    }

    /// <summary>
    ///     Puts <paramref name="npcUid"/>'s hands back as they were, however far the planting got. A charge that went on stays on.
    /// </summary>
    public void StopPlant(EntityUid npcUid)
    {
        if (!_plantingQuery.TryComp(npcUid, out var plantingComponent))
            return;

        if (plantingComponent.TakenOut is { } takenOut)
            _npcHandsSystem.PutBack(npcUid, takenOut);

        RemComp<NpcPlantingChargeComponent>(npcUid);
    }

    /// <summary>
    ///     Whether <paramref name="chargeUid"/> can still be set off from <paramref name="activatorUid"/>. Anyone can cut a wire.
    /// </summary>
    public bool IsWireIntact(EntityUid chargeUid, EntityUid activatorUid)
    {
        return _chainEndQuery.TryComp(chargeUid, out var chainEndComponent) &&
            chainEndComponent.StartUid == activatorUid &&
            _chainSystem.IsChainFullyIntact(activatorUid);
    }

    /// <summary>
    ///     Takes <paramref name="activatorUid"/> out and presses it, and gets the weapon back in hand, in one go. Returns whether
    ///         it was pressed.
    /// </summary>
    public bool TryDetonate(EntityUid npcUid, EntityUid activatorUid)
    {
        if (!_npcHandsSystem.TryTakeOut(npcUid, activatorUid, out var takenOut))
            return false;

        var pressed = _interactionSystem.UseInHandInteraction(npcUid, activatorUid);

        if (pressed)
            LetGoOfWire(activatorUid);

        _npcHandsSystem.PutBack(npcUid, takenOut);
        return pressed;
    }

    /// <summary>
    ///     Parts <paramref name="activatorUid"/> from its wire, which stays lying where it is. A wire done with goes no further:
    ///         one still joined to something in a bag is more than the engine can take when the bag is deleted.
    /// </summary>
    public void LetGoOfWire(EntityUid activatorUid)
    {
        if (!TerminatingOrDeleted(activatorUid))
            _chainSystem.TryBreakChainFrom(activatorUid, removeJoints: true);
    }
}
