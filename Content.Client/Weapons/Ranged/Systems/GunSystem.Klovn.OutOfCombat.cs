using Content.Client.Gameplay;
using Content.Shared._KS14.Weapons.Ranged;
using Content.Shared.Chemistry.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Interaction;
using Content.Shared.Storage;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Map;

namespace Content.Client.Weapons.Ranged.Systems;

// Partial: KsGunFiresOutOfCombatComponent's combat mode check, kept apart from the upstream update loop.
public sealed partial class GunSystem
{
    [Dependency] private SharedInteractionSystem _interactionSystem = default!;

    private bool _ksFireKeyWasDown;
    private bool _ksHoldFireUntilRelease;

    /// <summary>
    ///     Whether combat mode stops <paramref name="gun"/> firing this frame. An ordinary gun fires only in combat mode,
    ///         and one with <see cref="KsGunFiresOutOfCombatComponent"/> only out of it, where a click also interacts: a
    ///         press that lands on something the gun would interact with, such as a tank it refills from, holds fire until
    ///         the key is released.
    /// </summary>
    private bool KsCombatModeBlocksFire(Entity<GunComponent> gun, EntityUid userUid, bool inCombatMode, bool keyDown)
    {
        var pressed = keyDown && !_ksFireKeyWasDown;
        _ksFireKeyWasDown = keyDown;
        if (!keyDown)
            _ksHoldFireUntilRelease = false;

        if (!HasComp<KsGunFiresOutOfCombatComponent>(gun))
            return !inCombatMode;

        if (inCombatMode)
            return true;

        if (pressed)
            _ksHoldFireUntilRelease = KsClickInteracts(userUid);

        return _ksHoldFireUntilRelease;
    }

    private bool KsClickInteracts(EntityUid userUid)
    {
        if (_state.CurrentState is not GameplayStateBase gameplayState)
            return false;

        var mouseCoordinates = _eyeManager.PixelToMap(_inputManager.MouseScreenPosition);
        if (mouseCoordinates.MapId == MapId.Nullspace || gameplayState.GetClickedEntity(mouseCoordinates) is not { } targetUid)
            return false;

        if (!HasComp<RefillableSolutionComponent>(targetUid)
            && !HasComp<DrainableSolutionComponent>(targetUid)
            && !HasComp<StorageComponent>(targetUid)
            && !HasComp<ItemSlotsComponent>(targetUid))
            return false;

        return _interactionSystem.InRangeUnobstructed(userUid, targetUid);
    }
}
