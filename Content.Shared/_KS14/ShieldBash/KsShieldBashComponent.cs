using Content.Shared.Whitelist;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._KS14.ShieldBash;

/// <summary>
///     Hitting this shield with a melee weapon bangs on it, as a warning: a clang, an emote, and a shake.
///         tgstation's shield <c>item_interaction</c> with <c>/obj/item/melee</c>.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class KsShieldBashComponent : Component
{
    [DataField]
    public SoundSpecifier BashSound = new SoundPathSpecifier("/Audio/_KS14/Effects/Shields/shieldbash.ogg", AudioParams.Default.WithVariation(0.1f));

    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(3);

    /// <summary>
    ///     Narrows which melee weapons can bash it. By default anything with a melee attack can, other than
    ///         another shield.
    /// </summary>
    [DataField]
    public EntityWhitelist? Whitelist;

    /// <summary>
    ///     How the basher jitters. tgstation's <c>Shake(3, 3, 0.5 SECONDS)</c>: the smallest jitter is about 3 pixels.
    /// </summary>
    [DataField]
    public TimeSpan JitterTime = TimeSpan.FromSeconds(0.5);

    [DataField]
    public float JitterAmplitude = 1f;

    [DataField]
    public float JitterFrequency = 10f;
}
