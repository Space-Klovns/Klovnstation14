// SPDX-FileCopyrightText: 2025 github_actions[bot]
// SPDX-FileCopyrightText: 2025 nabegator220
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Atmos.Piping.Trinary.Components
{
    [Serializable, NetSerializable]
    public enum MolarMixerUiKey
    {
        Key,
    }

    [Serializable, NetSerializable]
    public sealed class MolarMixerBoundUserInterfaceState : BoundUserInterfaceState
    {
        public string MixerLabel { get; }
        public float OutputMolarFlow { get; }
        public bool Enabled { get; }

        public float NodeOne { get; }

        public MolarMixerBoundUserInterfaceState(string mixerLabel, float outputMolarFlow, bool enabled, float nodeOne)
        {
            MixerLabel = mixerLabel;
            OutputMolarFlow = outputMolarFlow;
            Enabled = enabled;
            NodeOne = nodeOne;
        }
    }

    [Serializable, NetSerializable]
    public sealed class MolarMixerToggleStatusMessage : BoundUserInterfaceMessage
    {
        public bool Enabled { get; }

        public MolarMixerToggleStatusMessage(bool enabled)
        {
            Enabled = enabled;
        }
    }

    [Serializable, NetSerializable]
    public sealed class MolarMixerChangeOutputMolarFlowMessage : BoundUserInterfaceMessage
    {
        public float MolarFlow { get; }

        public MolarMixerChangeOutputMolarFlowMessage(float molarFlow)
        {
            MolarFlow = molarFlow;
        }
    }

    [Serializable, NetSerializable]
    public sealed class MolarMixerChangeNodePercentageMessage : BoundUserInterfaceMessage
    {
        public float NodeOne { get; }

        public MolarMixerChangeNodePercentageMessage(float nodeOne)
        {
            NodeOne = nodeOne;
        }
    }
}
