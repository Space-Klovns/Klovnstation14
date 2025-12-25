// SPDX-FileCopyrightText: 2025 github_actions[bot]
// SPDX-FileCopyrightText: 2025 nabegator220
//
// SPDX-License-Identifier: MPL-2.0

using Content.Shared.Electrocution;
using Content.Shared.Construction;
using Content.Shared._KS14.Construction;
using Content.Server.Electrocution;
using Content.Server.Lightning;
using Content.Shared._KS14.ArcFlash.Components;

namespace Content.Server._KS14.ArcFlash;

public sealed class MindShieldSystem : EntitySystem
{
    [Dependency] private readonly ElectrocutionSystem _electrocutionSystem = default!;
    [Dependency] private readonly LightningSystem _lightning = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ArcFlashAnchorableComponent, AnchorStateChangedEvent>(OnAnchorChanged);
        SubscribeLocalEvent<ArcFlashDeconstructableComponent, MachineDeconstructedEvent>(OnDeconstruction);
        SubscribeLocalEvent<ArcFlashDeconstructableComponent, APCDeconstructedEvent>(OnAPCDeconstruction);
    }
    private void OnAnchorChanged(EntityUid uid, ArcFlashAnchorableComponent deviceComp, ref AnchorStateChangedEvent args)
    {
        if (args.Anchored)
            return; // we don't want to inflict arc flashing when the connection is created

        // anchor state can change as a result of deletion (detach to null) - same shit as cable system
        if (TerminatingOrDeleted(uid))
            return;

        ElectrifiedComponent? electrified = null;
        TransformComponent? transform = null;
        if (Resolve(uid, ref electrified, ref transform, false))
            if (_electrocutionSystem.IsPowered(uid, electrified, transform))
                _lightning.ShootRandomLightnings(uid, deviceComp.lightningRange, deviceComp.lightningAmount, lightningPrototype: deviceComp.lightningPrototype);
    }
    private void OnDeconstruction(EntityUid uid, ArcFlashDeconstructableComponent deviceComp, ref MachineDeconstructedEvent args)
    {
        //there is no way for us to check battery status anyway
        _lightning.ShootRandomLightnings(uid, deviceComp.lightningRange, deviceComp.lightningAmount, lightningPrototype: deviceComp.lightningPrototype);
    }
    private void OnAPCDeconstruction(EntityUid uid, ArcFlashDeconstructableComponent deviceComp, ref APCDeconstructedEvent args)
    {
        //there is no way for us to check battery status anyway
        _lightning.ShootRandomLightnings(uid, deviceComp.lightningRange, deviceComp.lightningAmount, lightningPrototype: deviceComp.lightningPrototype);
    }
}
