// SPDX-FileCopyrightText: 2019 Víctor Aguilera Puerto
// SPDX-FileCopyrightText: 2021 Acruid
// SPDX-FileCopyrightText: 2021 DrSmugleaf
// SPDX-FileCopyrightText: 2021 Visne
// SPDX-FileCopyrightText: 2022 metalgearsloth
// SPDX-FileCopyrightText: 2022 mirrorcult
// SPDX-FileCopyrightText: 2022 wrexbe
// SPDX-FileCopyrightText: 2023 Nemanja
// SPDX-FileCopyrightText: 2025 Gerkada
// SPDX-FileCopyrightText: 2025 github_actions[bot]
//
// SPDX-License-Identifier: MPL-2.0

using Robust.Shared.Serialization;
using Content.Shared._KS14.Research;

namespace Content.Shared._KS14.Research.Components
{
    [Serializable, NetSerializable]
    public sealed class FancyResearchConsoleState : BoundUserInterfaceState
    {
        public int Points;

        /// <summary>
        /// Goobstation field - all researches and their availablities
        /// </summary>
        public Dictionary<string, ResearchAvailability> Researches;

        public FancyResearchConsoleState(int points, Dictionary<string, ResearchAvailability> researches) // Goobstation R&D console rework = researches field
        {
            Points = points;
            Researches = researches; // Goobstation R&D console rework
        }
    }
}
