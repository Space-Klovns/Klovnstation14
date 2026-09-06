namespace Content.Shared.Actions.Events;

/// <summary>
///     Raised on the action entity when it is used and <see cref="BaseActionEvent.Handled"/>.
///         KS14: Subscribers must not mutate the passed <paramref name="ActionEvent"/>.
/// </summary>
/// <param name="Performer">The entity that performed this action.</param>
[ByRefEvent]
public readonly record struct ActionPerformedEvent(EntityUid Performer, BaseActionEvent ActionEvent /* KS14 */, bool Predicted /* KS14 */);
