// SPDX-FileCopyrightText: 2022 Flipp Syder
// SPDX-FileCopyrightText: 2023 DrSmugleaf
// SPDX-FileCopyrightText: 2025 Hagvan
//
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Client.SurveillanceCamera;

[RegisterComponent]
public sealed partial class ActiveSurveillanceCameraMonitorVisualsComponent : Component
{
    public float TimeLeft = 1f; // Goobstation - made switching faster. Node: it does not equal 3 seconds, prediction does some funny things

    public TimeSpan PreviousCurTime; // Goobstation

    public Action? OnFinish;
}
