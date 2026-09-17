namespace Content.Client._KS14.Graphics;

/// <summary>
///     Stable ids for this fork's post-shaders, so each system can replace or remove its own
///         entry without disturbing anyone else's. Mirrors upstream's ContentPostShaderIds.
/// </summary>
public static class KsPostShaderIds
{
    public const string LavaSinking = "ks-lava-sinking";
    public const string BotWranglerOutline = "ks-bot-wrangler-outline";
    public const string WaveDistortion = "ks-wave-distortion";
}
