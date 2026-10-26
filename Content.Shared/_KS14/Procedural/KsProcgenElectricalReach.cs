using System.Numerics;

namespace Content.Shared._KS14.Procedural;

/// <summary>Geometric reach only. Grid, anchoring, provider assignment and operating power need engine checks.</summary>
public static class KsProcgenElectricalReach
{
    public static bool WithinRange(Vector2 providerPosition, Vector2 receiverPosition, int transferRange, int receptionRange)
    {
        if (!float.IsFinite(providerPosition.X) || !float.IsFinite(providerPosition.Y) ||
            !float.IsFinite(receiverPosition.X) || !float.IsFinite(receiverPosition.Y) ||
            transferRange < 0 || receptionRange < 0)
            return false;
        // Match ExtensionCableSystem's float Length(), including its inclusive boundary.
        return (receiverPosition - providerPosition).Length() <= (float) Math.Min(transferRange, receptionRange);
    }
}
