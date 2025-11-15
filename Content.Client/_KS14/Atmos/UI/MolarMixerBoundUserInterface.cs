// SPDX-FileCopyrightText: 2021 E F R
// SPDX-FileCopyrightText: 2021 ike709
// SPDX-FileCopyrightText: 2022 Leon Friedrich
// SPDX-FileCopyrightText: 2022 Mervill
// SPDX-FileCopyrightText: 2022 mirrorcult
// SPDX-FileCopyrightText: 2023 Dawid Bla
// SPDX-FileCopyrightText: 2023 TemporalOroboros
// SPDX-FileCopyrightText: 2024 Kot
// SPDX-FileCopyrightText: 2024 Nemanja
// SPDX-FileCopyrightText: 2024 metalgearsloth
// SPDX-FileCopyrightText: 2025 github_actions[bot]
// SPDX-FileCopyrightText: 2025 nabegator220
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._KS14.Atmos;
using Content.Shared._KS14.Atmos.Piping.Trinary.Components;
using Content.Shared.Localizations;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._KS14.Atmos.UI
{
    /// <summary>
    /// Initializes a <see cref="MolarMixerWindow"/> and updates it when new server messages are received.
    /// </summary>
    [UsedImplicitly]
    public sealed class MolarMixerBoundUserInterface : BoundUserInterface
    {
        [ViewVariables]
        private const float MaxMolarFlow = KsAtmospherics.MaxMolarFlow;

        [ViewVariables]
        private MolarMixerWindow? _window;

        public MolarMixerBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
        {
        }

        protected override void Open()
        {
            base.Open();

            _window = this.CreateWindow<MolarMixerWindow>();

            _window.ToggleStatusButtonPressed += OnToggleStatusButtonPressed;
            _window.MixerOutputMolarFlowChanged += OnMixerOutputMolarFlowPressed;
            _window.MixerNodePercentageChanged += OnMixerSetPercentagePressed;
        }

        private void OnToggleStatusButtonPressed()
        {
            if (_window is null) return;
            SendMessage(new MolarMixerToggleStatusMessage(_window.MixerStatus));
        }

        private void OnMixerOutputMolarFlowPressed(string value)
        {
            var molarFlow = UserInputParser.TryFloat(value, out var parsed) ? parsed : 0f;
            if (molarFlow > MaxMolarFlow)
                molarFlow = MaxMolarFlow;

            SendMessage(new MolarMixerChangeOutputMolarFlowMessage(molarFlow));
        }

        private void OnMixerSetPercentagePressed(string value)
        {
            // We don't need to send both nodes because it's just 100.0f - node
            var node = UserInputParser.TryFloat(value, out var parsed) ? parsed : 1.0f;

            node = Math.Clamp(node, 0f, 100.0f);

            if (_window is not null)
                node = _window.NodeOneLastEdited ? node : 100.0f - node;

            SendMessage(new MolarMixerChangeNodePercentageMessage(node));
        }

        /// <summary>
        /// Update the UI state based on server-sent info
        /// </summary>
        /// <param name="state"></param>
        protected override void UpdateState(BoundUserInterfaceState state)
        {
            base.UpdateState(state);
            if (_window == null || state is not MolarMixerBoundUserInterfaceState cast)
                return;

            _window.Title = (cast.MixerLabel);
            _window.SetMixerStatus(cast.Enabled);
            _window.SetOutputMolarFlow(cast.OutputMolarFlow);
            _window.SetNodePercentages(cast.NodeOne);
        }
    }
}
