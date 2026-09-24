using Content.Shared._KS14.ZLevel.Transit;

namespace Content.Client._KS14.ZLevel.Transit;

/// <summary>
///     Nothing of its own: a gap map and its contents arrive replicated, and the progress through it is
///         driven from the shared half against a clock both sides agree on.
/// </summary>
/// <remarks>
///     The ride is rendered without any code here. A rider is standing on the gap map, so it is drawn as the
///         viewer's own map - one unscaled pass, through the real eye - and the z-levels below it come out of
///         <see cref="Content.Shared._KS14.ZLevel.KsZLevelSystem.TryGetZLevelsBelow"/> resolving the gap to
///         its anchor. That is the whole of why the platform now stays fixed underfoot while the world below
///         recedes, and it is why this class is empty.
/// </remarks>
public sealed partial class KsZLevelGapSystem : SharedKsZLevelGapSystem;
