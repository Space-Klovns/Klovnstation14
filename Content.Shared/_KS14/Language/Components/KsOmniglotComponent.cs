namespace Content.Shared._KS14.Language.Components;

/// <summary>
///     Understands every language (observers, admin shenanigans). Deliberately no such fallback
///     for entities merely lacking language components.
/// </summary>
[RegisterComponent]
public sealed partial class KsOmniglotComponent : Component;
