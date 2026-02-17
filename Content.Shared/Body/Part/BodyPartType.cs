// SPDX-FileCopyrightText: 2021 Visne
// SPDX-FileCopyrightText: 2021 mirrorcult
// SPDX-FileCopyrightText: 2022 DrSmugleaf
// SPDX-FileCopyrightText: 2022 Jezithyr
// SPDX-FileCopyrightText: 2022 ZeroDayDaemon
// SPDX-FileCopyrightText: 2022 metalgearsloth
// SPDX-FileCopyrightText: 2022 wrexbe
// SPDX-FileCopyrightText: 2026 LaCumbiaDelCoronavirus
//
// SPDX-License-Identifier: MPL-2.0

using Content.Shared.Body.Components;
using Robust.Shared.Serialization;

namespace Content.Shared.Body.Part
{
    /// <summary>
    ///     Defines the type of a <see cref="BodyComponent"/>.
    /// </summary>
    [Flags] // KS14 Klovnmed: added FlagsAttribute
    [Serializable, NetSerializable]
    public enum BodyPartType
    {
        Other = 0,
        Torso = 1 << 0 /* KS14 change: added value */,
        Head = 1 << 1 /* KS14 change: added value */,
        Arm = 1 << 2 /* KS14 change: added value */,
        Hand = 1 << 3 /* KS14 change: added value */,
        Leg = 1 << 4 /* KS14 change: added value */,
        Foot = 1 << 5 /* KS14 change: added value */,
        Tail = 1 << 6 /* KS14 change: added value */
    }
}
