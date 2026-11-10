namespace Content.Shared.Temperature
{
    /// <summary>
    ///     Directed event raised on entities to query whether they're "hot" or not.
    ///     For example, a lit welder or matchstick would be hot, etc.
    /// </summary>
    // KS14: sealed class : EntityEventArgs -> by-ref record struct, so asking doesn't allocate. Not a C# ref struct,
    //     since the event bus's generic raise and subscribe methods don't allow one as a type argument.
    [ByRefEvent] // KS14
    public /* KS14: sealed class -> record struct */ record struct IsHotEvent /* KS14: removed ': EntityEventArgs' */
    {
        public bool IsHot { get; set; } /* KS14: removed '= false', a struct field initializer needs a declared constructor */
    }
}
