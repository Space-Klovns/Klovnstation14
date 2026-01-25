// SPDX-FileCopyrightText: 2026 LaCumbiaDelCoronavirus
//
// SPDX-License-Identifier: MPL-2.0

using Content.Shared.Administration;
using Content.Shared.CCVar.CVarAccess;
using Robust.Shared.Configuration;

namespace Content.Shared._KS14.CCVar;

public sealed partial class KsCCVars
{
    /// <summary>
    ///     Should overlay stains be drawn more expensively?
    /// </summary>
    [CVarControl(AdminFlags.Debug)]
    public static readonly CVarDef<bool> ComplexStainDrawing =
        CVarDef.Create("klovn.stains.complexdrawing", false, CVar.CLIENT | CVar.CLIENTONLY); // TODO LCDC FUCK: FIX THIS ASAP
}
