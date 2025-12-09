// SPDX-FileCopyrightText: 2025 Gerkada
//
// SPDX-License-Identifier: MIT

using Robust.Shared.Serialization;

namespace Content.Shared.Magic.Events;

[Serializable, NetSerializable]
public sealed class StopTargetingEvent : EntityEventArgs
{
}
