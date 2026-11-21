using Content.Shared.Actions;
using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Cultivation.Components;

public enum WuxiaStyle : byte
{
    DrunkenFist,
    EagleClaw,
    WingChun,
}

/// <summary>
///     A wuxia fighting style learned from a manual. Punches (H), shoves (D) and grabs (G) build a streak, and the
///         right streak lands one of the style's combos.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WuxiaStyleComponent : Component
{
    [DataField(required: true)]
    public WuxiaStyle Style;

    /// <summary>
    ///     Recent inputs, newest last.
    /// </summary>
    [DataField]
    public string Streak = string.Empty;

    [DataField]
    public EntityUid? StreakTarget;

    [DataField]
    public TimeSpan LastInput;

    /// <summary>
    ///     Wing Chun's Sticky Hands: until when the next melee blow is turned aside.
    /// </summary>
    [DataField]
    public TimeSpan StickyUntil;
}

/// <summary>
///     Lists the style's combos.
/// </summary>
public sealed partial class RecallTeachingsActionEvent : InstantActionEvent;
