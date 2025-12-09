// SPDX-FileCopyrightText: 2025 Gerkada
// SPDX-FileCopyrightText: 2025 github_actions[bot]
//
// SPDX-License-Identifier: MIT

using Robust.Shared.Serialization;

namespace Content.Shared.Magic.Events;

[Serializable, NetSerializable]
public sealed class StopTargetingEvent : EntityEventArgs
{
}
