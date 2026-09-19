using Content.Client.Graphics;

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

    /// <summary>
    ///     Every outline there is, upstream's and this fork's, for a base effect to order itself before.
    /// </summary>
    /// <remarks>
    ///     Use these rather than the upstream arrays directly: ordering is a two-way relation, so a fork
    ///         shader that only referenced <see cref="ContentPostShaderIds"/> would end up unordered against
    ///         the fork's own shaders, and land wherever insertion order happened to put it.
    /// </remarks>
    public static readonly string[] BeforeOutlines =
    [
        ..ContentPostShaderIds.BeforeOutlines,
        BotWranglerOutline,
    ];

    /// <summary>
    ///     Every base effect there is, upstream's and this fork's, for an outline to order itself after.
    /// </summary>
    public static readonly string[] AfterBaseEffects =
    [
        ..ContentPostShaderIds.AfterBaseEffects,
        LavaSinking,
        WaveDistortion,
    ];
}
