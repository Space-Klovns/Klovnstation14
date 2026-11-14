namespace Content.Shared._KS14.Despawn;

/// <summary>
///     Raised, broadcast, when a gun has dropped a cartridge or shell on the floor
///         (<c>SharedGunSystem.EjectCartridge</c>), whether it is spent or not. Both sides raise it; only the
///         server listens.
/// </summary>
/// <param name="Cartridge">The cartridge that was ejected.</param>
[ByRefEvent]
public readonly record struct KsCartridgeEjectedEvent(EntityUid Cartridge);
