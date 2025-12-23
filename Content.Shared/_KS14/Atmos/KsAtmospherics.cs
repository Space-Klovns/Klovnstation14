// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2025 github_actions[bot]
// SPDX-FileCopyrightText: 2025 nabegator220
//
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Shared._KS14.Atmos;

/// <summary>
///     Class to store atmos constants for KS14.
/// </summary>
public static class KsAtmospherics
{
    /// <summary>
    ///     Zipion reaction begins when you reach this temperature or lower
    /// </summary>
    public const float ZipionProductionThresholdTemperature = 200f;
    /// <summary>
    ///     Zipion reaction rate - 1/x of the plasma is converted into Zipion each tick
    /// </summary>
    public const float ZipionProductionConversionRate = 40f;
    /// <summary>
    ///     The default molar flow at which molar equipment maxes out at, in mol/s.
    /// </summary>
    public const float MaxMolarFlow = 400;
}
