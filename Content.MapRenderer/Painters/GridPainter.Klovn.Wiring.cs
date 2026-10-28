using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server.Power.Components;
using SixLabors.ImageSharp;

namespace Content.MapRenderer.Painters;

public sealed partial class GridPainter : IDisposable
{
    private readonly bool _drawWiresLast;

    private void PaintEntities(Image canvas, List<EntityData> entities, Vector2 offset)
    {
        if (!_drawWiresLast)
        {
            _entityPainter.Run(canvas, entities, offset);
            return;
        }
        var cables = entities.Where(entity => _sEntityManager.HasComponent<CableComponent>(entity.Owner)).ToList();
        var ordinary = entities.Where(entity => !_sEntityManager.HasComponent<CableComponent>(entity.Owner)).ToList();
        _entityPainter.Run(canvas, ordinary, offset);
        _entityPainter.Run(canvas, cables, offset);
    }

    public void Dispose() => _entityPainter.Dispose();
}
