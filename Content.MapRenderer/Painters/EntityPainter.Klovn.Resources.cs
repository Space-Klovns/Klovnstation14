using System;
using System.Linq;

namespace Content.MapRenderer.Painters;

public sealed partial class EntityPainter : IDisposable
{
    public void Dispose()
    {
        foreach (var image in _images.Values.Distinct()) image.Dispose();
        _images.Clear();
        _errorImage.Dispose();
    }
}
