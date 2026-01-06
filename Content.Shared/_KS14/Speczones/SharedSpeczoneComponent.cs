// SPDX-FileCopyrightText: 2026 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2026 github_actions[bot]
//
// SPDX-License-Identifier: MPL-2.0

using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Speczones;

[Access(typeof(SharedSpeczoneSystem))]
[NetworkedComponent]
public abstract partial class SharedSpeczoneComponent : Component;
