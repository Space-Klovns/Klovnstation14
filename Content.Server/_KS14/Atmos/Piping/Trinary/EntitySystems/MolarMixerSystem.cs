// SPDX-FileCopyrightText: 2021 20kdc
// SPDX-FileCopyrightText: 2021 E F R
// SPDX-FileCopyrightText: 2021 Pieter-Jan Briers
// SPDX-FileCopyrightText: 2021 ike709
// SPDX-FileCopyrightText: 2022 Moony
// SPDX-FileCopyrightText: 2022 Vera Aguilera Puerto
// SPDX-FileCopyrightText: 2022 theashtronaut
// SPDX-FileCopyrightText: 2022 wrexbe
// SPDX-FileCopyrightText: 2023 Kara
// SPDX-FileCopyrightText: 2023 Visne
// SPDX-FileCopyrightText: 2023 faint
// SPDX-FileCopyrightText: 2024 Leon Friedrich
// SPDX-FileCopyrightText: 2024 Nemanja
// SPDX-FileCopyrightText: 2024 metalgearsloth
// SPDX-FileCopyrightText: 2024 slarticodefast
// SPDX-FileCopyrightText: 2024 themias
// SPDX-FileCopyrightText: 2025 Tayrtahn
// SPDX-FileCopyrightText: 2025 github_actions[bot]
// SPDX-FileCopyrightText: 2025 nabegator220
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server.Administration.Logs;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Atmos.Piping.Components;
using Content.Server.Atmos.Piping.Trinary.Components;
using Content.Server.NodeContainer;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Piping;
using Content.Shared.Atmos.Piping.Components;
using Content.Shared.Atmos.Piping.Trinary.Components;
using Content.Shared.Audio;
using Content.Shared.Database;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using JetBrains.Annotations;
using Robust.Server.GameObjects;
using Robust.Shared.Player;
using Content.Server._KS14.Atmos.Piping.Trinary.Components;
using Content.Shared._KS14.Atmos.Piping.Trinary.Components;

namespace Content.Server._KS14.Atmos.Piping.Trinary.EntitySystems
{
    [UsedImplicitly]
    public sealed class MolarMixerSystem : EntitySystem
    {
        [Dependency] private UserInterfaceSystem _userInterfaceSystem = default!;
        [Dependency] private IAdminLogManager _adminLogger = default!;
        [Dependency] private readonly AtmosphereSystem _atmosphereSystem = default!;
        [Dependency] private readonly SharedAmbientSoundSystem _ambientSoundSystem = default!;
        [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
        [Dependency] private readonly NodeContainerSystem _nodeContainer = default!;
        [Dependency] private readonly SharedPopupSystem _popup = default!;

        public override void Initialize()
        {
            base.Initialize();

            SubscribeLocalEvent<MolarMixerComponent, ComponentInit>(OnInit);
            SubscribeLocalEvent<MolarMixerComponent, AtmosDeviceUpdateEvent>(OnMixerUpdated);
            SubscribeLocalEvent<MolarMixerComponent, ActivateInWorldEvent>(OnMixerActivate);
            SubscribeLocalEvent<MolarMixerComponent, GasAnalyzerScanEvent>(OnMixerAnalyzed);
            // Bound UI subscriptions
            SubscribeLocalEvent<MolarMixerComponent, MolarMixerChangeOutputMolarFlowMessage>(OnOutputMolarFlowChangeMessage);
            SubscribeLocalEvent<MolarMixerComponent, MolarMixerChangeNodePercentageMessage>(OnChangeNodePercentageMessage);
            SubscribeLocalEvent<MolarMixerComponent, MolarMixerToggleStatusMessage>(OnToggleStatusMessage);

            SubscribeLocalEvent<MolarMixerComponent, AtmosDeviceDisabledEvent>(OnMixerLeaveAtmosphere);
        }

        private void OnInit(EntityUid uid, MolarMixerComponent mixer, ComponentInit args)
        {
            UpdateAppearance(uid, mixer);
        }

        private void OnMixerUpdated(EntityUid uid, MolarMixerComponent mixer, ref AtmosDeviceUpdateEvent args)
        {
            // TODO ATMOS: Cache total moles since it's expensive.
            // TODO MAYBE molar mixer: right now it only checks if it's already overpressured, but since each step can potentially bring massive pressure increases this can hit multiple times its max pressure threshold
            // I'm keeping it this way because you're going to regulate the pressure with a pump anyway and it's really not that big of a deal.

            if (!mixer.Enabled
                || !_nodeContainer.TryGetNodes(uid, mixer.InletOneName, mixer.InletTwoName, mixer.OutletName, out PipeNode? inletOne, out PipeNode? inletTwo, out PipeNode? outlet))
            {
                _ambientSoundSystem.SetAmbience(uid, false);
                return;
            }

            var outputStartingPressure = outlet.Air.Pressure;

            if (outputStartingPressure >= Atmospherics.MaxOutputPressure)
                return; // Overpressure

            var transferMolesOne = mixer.TargetMolarFlow * mixer.InletOneConcentration;
            var transferMolesTwo = mixer.TargetMolarFlow - transferMolesOne;

            //just check if any of the inputs is set to 100%, then initiate molar pump mode (ie pump even if undersupplied). gas mixer does this so this will do it too
            if (mixer.InletOneConcentration <= 0f)
            {
                if (inletTwo.Air.Temperature <= 0f)
                    return;

                transferMolesOne = 0f;
                transferMolesTwo = MathF.Min(transferMolesTwo, inletTwo.Air.TotalMoles);
            }
            else if (mixer.InletOneConcentration >= 100f)
            {
                if (inletOne.Air.Temperature <= 0f)
                    return;

                transferMolesOne = MathF.Min(transferMolesOne, inletOne.Air.TotalMoles);
                transferMolesTwo = 0f;
            }
            else
            {
                if (inletOne.Air.TotalMoles < transferMolesOne || inletTwo.Air.TotalMoles < transferMolesTwo) //check for undersupply when properly mixing
                    return;
            }

            // begin transfer
            var transferred = false;

            if (transferMolesOne > 0f)
            {
                transferred = true;
                var removed = inletOne.Air.Remove(transferMolesOne);
                _atmosphereSystem.Merge(outlet.Air, removed);
            }

            if (transferMolesTwo > 0f)
            {
                transferred = true;
                var removed = inletTwo.Air.Remove(transferMolesTwo);
                _atmosphereSystem.Merge(outlet.Air, removed);
            }

            if (transferred)
                _ambientSoundSystem.SetAmbience(uid, true);
        }

        private void OnMixerLeaveAtmosphere(EntityUid uid, MolarMixerComponent mixer, ref AtmosDeviceDisabledEvent args)
        {
            mixer.Enabled = false;

            DirtyUI(uid, mixer);
            UpdateAppearance(uid, mixer);
            _userInterfaceSystem.CloseUi(uid, GasFilterUiKey.Key);
        }

        private void OnMixerActivate(EntityUid uid, MolarMixerComponent mixer, ActivateInWorldEvent args)
        {
            if (args.Handled || !args.Complex)
                return;

            if (!TryComp(args.User, out ActorComponent? actor))
                return;

            if (Transform(uid).Anchored)
            {
                _userInterfaceSystem.OpenUi(uid, MolarMixerUiKey.Key, actor.PlayerSession);
                DirtyUI(uid, mixer);
            }
            else
            {
                _popup.PopupCursor(Loc.GetString("comp-gas-mixer-ui-needs-anchor"), args.User);
            }

            args.Handled = true;
        }

        private void DirtyUI(EntityUid uid, MolarMixerComponent? mixer)
        {
            if (!Resolve(uid, ref mixer))
                return;

            _userInterfaceSystem.SetUiState(uid, MolarMixerUiKey.Key,
                new MolarMixerBoundUserInterfaceState(Comp<MetaDataComponent>(uid).EntityName, mixer.TargetMolarFlow, mixer.Enabled, mixer.InletOneConcentration));
        }

        private void UpdateAppearance(EntityUid uid, MolarMixerComponent? mixer = null, AppearanceComponent? appearance = null)
        {
            if (!Resolve(uid, ref mixer, ref appearance, false))
                return;

            _appearance.SetData(uid, FilterVisuals.Enabled, mixer.Enabled, appearance);
        }

        private void OnToggleStatusMessage(EntityUid uid, MolarMixerComponent mixer, MolarMixerToggleStatusMessage args)
        {
            mixer.Enabled = args.Enabled;
            _adminLogger.Add(LogType.AtmosPowerChanged, LogImpact.Medium,
                $"{ToPrettyString(args.Actor):player} set the power on {ToPrettyString(uid):device} to {args.Enabled}");
            DirtyUI(uid, mixer);
            UpdateAppearance(uid, mixer);
        }

        private void OnOutputMolarFlowChangeMessage(EntityUid uid, MolarMixerComponent mixer, MolarMixerChangeOutputMolarFlowMessage args)
        {
            mixer.TargetMolarFlow = Math.Clamp(args.MolarFlow, 0f, mixer.MaxTargetMolarFlow);
            _adminLogger.Add(LogType.AtmosMolarFlowChanged, LogImpact.Medium,
                $"{ToPrettyString(args.Actor):player} set the molar flow on {ToPrettyString(uid):device} to {args.MolarFlow}kPa");
            DirtyUI(uid, mixer);
        }

        private void OnChangeNodePercentageMessage(EntityUid uid, MolarMixerComponent mixer,
            MolarMixerChangeNodePercentageMessage args)
        {
            float nodeOne = Math.Clamp(args.NodeOne, 0f, 100.0f) / 100.0f;
            mixer.InletOneConcentration = nodeOne;
            mixer.InletTwoConcentration = 1.0f - mixer.InletOneConcentration;
            _adminLogger.Add(LogType.AtmosRatioChanged, LogImpact.Medium,
                $"{ToPrettyString(args.Actor):player} set the ratio on {ToPrettyString(uid):device} to {mixer.InletOneConcentration}:{mixer.InletTwoConcentration}");
            DirtyUI(uid, mixer);
        }

        /// <summary>
        /// Returns the gas mixture for the gas analyzer
        /// </summary>
        private void OnMixerAnalyzed(EntityUid uid, MolarMixerComponent component, GasAnalyzerScanEvent args)
        {
            args.GasMixtures ??= new List<(string, GasMixture?)>();

            // multiply by volume fraction to make sure to send only the gas inside the analyzed pipe element, not the whole pipe system
            if (_nodeContainer.TryGetNode(uid, component.InletOneName, out PipeNode? inletOne) && inletOne.Air.Volume != 0f)
            {
                var inletOneAirLocal = inletOne.Air.Clone();
                inletOneAirLocal.Multiply(inletOne.Volume / inletOne.Air.Volume);
                inletOneAirLocal.Volume = inletOne.Volume;
                args.GasMixtures.Add(($"{inletOne.CurrentPipeDirection} {Loc.GetString("gas-analyzer-window-text-inlet")}", inletOneAirLocal));
            }
            if (_nodeContainer.TryGetNode(uid, component.InletTwoName, out PipeNode? inletTwo) && inletTwo.Air.Volume != 0f)
            {
                var inletTwoAirLocal = inletTwo.Air.Clone();
                inletTwoAirLocal.Multiply(inletTwo.Volume / inletTwo.Air.Volume);
                inletTwoAirLocal.Volume = inletTwo.Volume;
                args.GasMixtures.Add(($"{inletTwo.CurrentPipeDirection} {Loc.GetString("gas-analyzer-window-text-inlet")}", inletTwoAirLocal));
            }
            if (_nodeContainer.TryGetNode(uid, component.OutletName, out PipeNode? outlet) && outlet.Air.Volume != 0f)
            {
                var outletAirLocal = outlet.Air.Clone();
                outletAirLocal.Multiply(outlet.Volume / outlet.Air.Volume);
                outletAirLocal.Volume = outlet.Volume;
                args.GasMixtures.Add((Loc.GetString("gas-analyzer-window-text-outlet"), outletAirLocal));
            }

            args.DeviceFlipped = inletOne != null && inletTwo != null && inletOne.CurrentPipeDirection.ToDirection() == inletTwo.CurrentPipeDirection.ToDirection().GetClockwise90Degrees();
        }
    }
}
