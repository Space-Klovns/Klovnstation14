// SPDX-FileCopyrightText: 2026 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2026 github_actions[bot]
//
// SPDX-License-Identifier: MPL-2.0

using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._KS14.Speczones;

/// <summary>
///     Specifies a map to load as a speczone.
/// </summary>
[Prototype]
public sealed partial class SpeczonePrototype : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    ///     Path to the map to load as the speczone.
    /// </summary>
    [DataField(required: true)]
    public ResPath MapPath { get; private set; } = default;
}
