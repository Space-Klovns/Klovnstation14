// SPDX-FileCopyrightText: 2025 FrauZj
// SPDX-FileCopyrightText: 2026 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2026 github_actions[bot]
//
// SPDX-License-Identifier: MPL-2.0

namespace Content.Shared._Goobstation.Emoting;

/// <summary>
///     Marks entities that can use animated emotes.
/// </summary>
[RegisterComponent]
public sealed partial class AnimatedEmotesComponent : Component;

[DataDefinition] public sealed partial class AnimationFlipEmoteEvent;
[DataDefinition] public sealed partial class AnimationSpinEmoteEvent;
[DataDefinition] public sealed partial class AnimationJumpEmoteEvent;
