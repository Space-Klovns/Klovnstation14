using Content.Shared._KS14.Weapons.Ranged;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Perks.Damage;

/// <summary>
///     The magazine drops out of the gun with a beep as soon as its last round is taken, as a gun with
///         <see cref="MagazineAmmoProviderComponent.AutoEject"/> does, for whatever gun the holder fires.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class KsMagazineAutoDropAbilityComponent : Component
{
    [DataField, AutoNetworkedField]
    public SoundSpecifier? Sound = new SoundPathSpecifier("/Audio/Weapons/Guns/EmptyAlarm/smg_empty_alarm.ogg");
}

public sealed partial class KsMagazineAutoDropSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audioSystem = default!;
    [Dependency] private ItemSlotsSystem _itemSlotsSystem = default!;
    [Dependency] private KsPerkGunSystem _perkGunSystem = default!;
    [Dependency] private EntityQuery<MagazineAmmoProviderComponent> _magazineProviderQuery = default!;
    [Dependency] private EntityQuery<ChamberMagazineAmmoProviderComponent> _chamberMagazineProviderQuery = default!;

    [SubscribeLocalEvent]
    private void OnFired(Entity<KsMagazineAutoDropAbilityComponent> entity, ref KsShooterFiredGunEvent args)
    {
        var gunUid = args.Gun.Owner;

        // A gun that does this already does it itself, beep and all.
        MagazineAmmoProviderComponent? providerComponent = _chamberMagazineProviderQuery.CompOrNull(gunUid);
        providerComponent ??= _magazineProviderQuery.CompOrNull(gunUid);
        if (providerComponent == null || providerComponent.AutoEject)
            return;

        if (!_perkGunSystem.TryGetMagazine(gunUid, out var magazineUid) ||
            _perkGunSystem.GetAmmoCount(magazineUid) > 0)
            return;

        if (_itemSlotsSystem.TryEject(gunUid, SharedGunSystem.MagazineSlot, null, out _, excludeUserAudio: true))
            _audioSystem.PlayPredicted(entity.Comp.Sound, gunUid, entity.Owner);
    }
}
