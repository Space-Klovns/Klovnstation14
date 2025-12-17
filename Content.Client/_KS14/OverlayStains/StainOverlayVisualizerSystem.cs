// SPDX-FileCopyrightText: 2025 LaCumbiaDelCoronavirus
//
// SPDX-License-Identifier: MIT

using Content.Client.Light;
using Robust.Client.Graphics;
using Robust.Shared.Utility;

namespace Content.Client._KS14.OverlayStains;

public sealed class StainOverlayVisualizerSystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlayManager = default!;

    private StainOverlay _stainOverlay = default!;

    public override void Initialize()
    {
        base.Initialize();

        _stainOverlay = new() { StainSpriteSpecifier = new SpriteSpecifier.Rsi(new ResPath("/Textures/Effects/crayondecals.rsi"), "splatter") };
        _overlayManager.AddOverlay(_stainOverlay);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlayManager.RemoveOverlay<StainOverlay>();
    }
}
