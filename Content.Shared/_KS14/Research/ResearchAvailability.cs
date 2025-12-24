// SPDX-FileCopyrightText: 2025 Aiden
// SPDX-FileCopyrightText: 2025 FaDeOkno
// SPDX-FileCopyrightText: 2025 Gerkada
// SPDX-FileCopyrightText: 2025 coderabbitai[bot]
// SPDX-FileCopyrightText: 2025 github_actions[bot]
// SPDX-FileCopyrightText: 2025 gluesniffler
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Research;

[Serializable, NetSerializable]
public enum ResearchAvailability : byte
{
    Researched,
    Available,
    PrereqsMet,
    Unavailable
}
