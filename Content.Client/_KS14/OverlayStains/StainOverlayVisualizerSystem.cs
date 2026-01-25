// SPDX-FileCopyrightText: 2026 LaCumbiaDelCoronavirus
//
// SPDX-License-Identifier: MIT

using Content.Client.Light;
using Content.Shared._KS14.CCVar;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;
using Robust.Shared.Utility;

namespace Content.Client._KS14.OverlayStains;

public sealed class StainOverlayVisualizerSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _configurationManager = default!;
    [Dependency] private readonly IOverlayManager _overlayManager = default!;

    private StainOverlay _stainOverlay = default!;

    public override void Initialize()
    {
        base.Initialize();

        _stainOverlay = new() { StainSpriteSpecifier = new SpriteSpecifier.Rsi(new ResPath("/Textures/Effects/crayondecals.rsi"), "splatter") };
        _overlayManager.AddOverlay(_stainOverlay);

        _configurationManager.OnValueChanged(KsCCVars.ComplexStainDrawing, (complexDrawing) => _stainOverlay.ComplexDrawing = complexDrawing, invokeImmediately: true);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlayManager.RemoveOverlay<StainOverlay>();
    }
}
